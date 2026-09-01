using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BackupManager.Models;

namespace BackupManager.Services;

public interface IResticService
{
    Task<string> GetVersionAsync(CancellationToken token = default);
    Task<bool> IsRepositoryInitializedAsync(RepositoryConfig repo, CancellationToken token = default);
    Task<InitResult> InitRepositoryAsync(RepositoryConfig repo, CancellationToken token = default);
    Task<List<SnapshotInfo>> GetSnapshotsAsync(RepositoryConfig repo, CancellationToken token = default);
    Task<(bool Success, long BytesAdded)> BackupAsync(
        RepositoryConfig repo, BackupJob job, IProgress<BackupProgress> progress, CancellationToken token);
    Task RestoreAsync(RepositoryConfig repo, string snapshotId, string targetDir,
        List<string>? paths, CancellationToken token);
    Task CheckAsync(RepositoryConfig repo, IProgress<string> progress, CancellationToken token);
    Task<List<string>> ListSnapshotContentsAsync(RepositoryConfig repo, string snapshotId, CancellationToken token);
}

public record InitResult(bool Success, bool AlreadyExisted, string Message);

public class ResticService : IResticService
{
    private readonly IConfigStore _config;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ResticService(IConfigStore config)
    {
        _config = config;
    }

    private string ResticPath => string.IsNullOrWhiteSpace(_config.Config.Settings.ResticPath)
        ? "restic"
        : _config.Config.Settings.ResticPath;

    private ProcessStartInfo BuildStartInfo(RepositoryConfig repo, params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ResticPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in arguments)
            psi.ArgumentList.Add(a);
        psi.Environment["RESTIC_REPOSITORY"] = repo.Location;
        psi.Environment["RESTIC_PASSWORD"] = repo.Password;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RESTIC_CACHE_DIR")))
            psi.Environment["RESTIC_CACHE_DIR"] =
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BackupManager", "restic-cache");
        return psi;
    }

    public async Task<string> GetVersionAsync(CancellationToken token = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ResticPath,
            ArgumentList = { "version" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start restic.");
        var output = await proc.StandardOutput.ReadToEndAsync(token);
        await proc.WaitForExitAsync(token);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException((await proc.StandardError.ReadToEndAsync(token)).Trim());
        return output.Trim();
    }

    public async Task<bool> IsRepositoryInitializedAsync(RepositoryConfig repo, CancellationToken token = default)
    {
        try
        {
            var psi = BuildStartInfo(repo, "snapshots", "--json", "--last", "1");
            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start restic.");
            await proc.WaitForExitAsync(token);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<InitResult> InitRepositoryAsync(RepositoryConfig repo, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(repo.Location) || string.IsNullOrWhiteSpace(repo.Password))
            return new InitResult(false, false, "Source location and password are required.");

        // Non-destructive by design: never overwrite or re-initialize an existing
        // repository. If something already exists at the remote location we leave it
        // untouched and just report that. The local copy folder is never touched here.
        if (await IsRepositoryInitializedAsync(repo, token))
            return new InitResult(true, true, "A source already exists at this location. Nothing was changed.");

        var psi = BuildStartInfo(repo, "init");
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start restic.");
        var err = await proc.StandardError.ReadToEndAsync(token);
        await proc.WaitForExitAsync(token);
        if (proc.ExitCode != 0)
            return new InitResult(false, false, err.Trim());
        return new InitResult(true, false, "Source initialized.");
    }

    public async Task<List<SnapshotInfo>> GetSnapshotsAsync(RepositoryConfig repo, CancellationToken token = default)
    {
        var psi = BuildStartInfo(repo, "snapshots", "--json");
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start restic.");
        var output = await proc.StandardOutput.ReadToEndAsync(token);
        var err = await proc.StandardError.ReadToEndAsync(token);
        await proc.WaitForExitAsync(token);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException(err.Trim());
        if (string.IsNullOrWhiteSpace(output))
            return new List<SnapshotInfo>();
        return JsonSerializer.Deserialize<List<SnapshotInfo>>(output, _jsonOptions) ?? new List<SnapshotInfo>();
    }

    public async Task<(bool Success, long BytesAdded)> BackupAsync(
        RepositoryConfig repo, BackupJob job, IProgress<BackupProgress> progress, CancellationToken token)
    {
        var args = new List<string> { "backup", "--json" };
        if (string.IsNullOrWhiteSpace(repo.LocalPath) || !Directory.Exists(repo.LocalPath))
        {
            progress.Report(new BackupProgress
            {
                Finished = true,
                Error = true,
                Message = "Local copy folder is missing or not set on the source."
            });
            return (false, 0);
        }
        args.Add(repo.LocalPath);
        foreach (var e in job.Excludes)
            args.AddRange(new[] { "--exclude", e });
        foreach (var t in job.Tags)
            args.AddRange(new[] { "--tag", t });

        var psi = BuildStartInfo(repo, args.ToArray());
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start restic.");
        long bytesAdded = 0;
        var reader = proc.StandardOutput;
        var sbErr = new StringBuilder();

        _ = Task.Run(async () =>
        {
            var errLine = await proc.StandardError.ReadToEndAsync(token);
            sbErr.Append(errLine);
        }, token);

        string? line;
        while ((line = await reader.ReadLineAsync(token)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("message_type", out var mt))
                    continue;
                var type = mt.GetString();
                if (type == "status")
                {
                    var pct = root.TryGetProperty("percent_done", out var p) ? p.GetDouble() * 100 : 0;
                    var bp = new BackupProgress
                    {
                        Percent = pct,
                        FilesDone = root.TryGetProperty("files_done", out var fd) ? fd.GetInt64() : 0,
                        FilesTotal = root.TryGetProperty("total_files", out var ft) ? ft.GetInt64() : 0,
                        BytesDone = root.TryGetProperty("bytes_done", out var bd) ? bd.GetInt64() : 0,
                        BytesTotal = root.TryGetProperty("total_bytes", out var bt) ? bt.GetInt64() : 0,
                        CurrentFile = root.TryGetProperty("current_file", out var cf) ? cf.GetString() : null
                    };
                    progress.Report(bp);
                }
                else if (type == "summary")
                {
                    bytesAdded = root.TryGetProperty("data_added", out var da) ? da.GetInt64() : 0;
                    progress.Report(new BackupProgress
                    {
                        Finished = true,
                        Percent = 100,
                        FilesDone = root.TryGetProperty("files_new", out var fn) ? fn.GetInt64() : 0,
                        FilesTotal = root.TryGetProperty("files_new", out var fnt) ? fnt.GetInt64() : 0,
                        Message = "Backup finished."
                    });
                }
                else if (type == "error")
                {
                    var msg = root.TryGetProperty("error", out var e) ? e.GetString() : "Unknown error";
                    progress.Report(new BackupProgress { Error = true, Message = msg ?? "Error" });
                }
            }
            catch (JsonException)
            {
                // Non-JSON progress line; ignore.
            }
        }

        await proc.WaitForExitAsync(token);
        if (proc.ExitCode != 0 && !progressAsFinished(progress))
            throw new InvalidOperationException(sbErr.ToString().Trim());

        return (proc.ExitCode == 0, bytesAdded);

        bool progressAsFinished(IProgress<BackupProgress> p)
        {
            p.Report(new BackupProgress { Finished = true, Error = true, Message = sbErr.ToString().Trim() });
            return true;
        }
    }

    public async Task RestoreAsync(RepositoryConfig repo, string snapshotId, string targetDir,
        List<string>? paths, CancellationToken token)
    {
        var args = new List<string> { "restore", snapshotId, "--target", targetDir };
        if (paths is { Count: > 0 })
        {
            args.Add("--include");
            args.Add(string.Join(" ", paths));
        }
        var psi = BuildStartInfo(repo, args.ToArray());
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start restic.");
        var err = await proc.StandardError.ReadToEndAsync(token);
        await proc.WaitForExitAsync(token);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException(err.Trim());
    }

    public async Task CheckAsync(RepositoryConfig repo, IProgress<string> progress, CancellationToken token)
    {
        var psi = BuildStartInfo(repo, "check");
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start restic.");
        _ = Task.Run(async () =>
        {
            string? line;
            while ((line = await proc.StandardError.ReadLineAsync(token)) is not null)
                progress.Report(line);
        }, token);
        await proc.WaitForExitAsync(token);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException("Source check failed. See output.");
    }

    public async Task<List<string>> ListSnapshotContentsAsync(RepositoryConfig repo, string snapshotId, CancellationToken token)
    {
        var psi = BuildStartInfo(repo, "ls", "--json", snapshotId);
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start restic.");
        var output = await proc.StandardOutput.ReadToEndAsync(token);
        var err = await proc.StandardError.ReadToEndAsync(token);
        await proc.WaitForExitAsync(token);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException(err.Trim());
        var result = new List<string>();
        using var reader = new StringReader(output);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var n in nodes.EnumerateArray())
                        if (n.TryGetProperty("path", out var p))
                            result.Add(p.GetString() ?? "");
                }
                else if (root.TryGetProperty("path", out var p))
                {
                    result.Add(p.GetString() ?? "");
                }
            }
            catch (JsonException)
            {
                // Ignore malformed lines.
            }
        }
        return result;
    }
}
