using BackupManager.Models;

namespace BackupManager.Services;

public class BackupStateChangedArgs : EventArgs
{
    public bool IsRunning { get; init; }
    public string JobName { get; init; } = "";
}

public interface ISchedulerService
{
    bool IsPaused { get; set; }
    void Start();
    void Stop();
    Task RunJobAsync(BackupJob job, bool manual = false, CancellationToken token = default);
    event EventHandler<RunLogEntry>? RunCompleted;
    event EventHandler<BackupStateChangedArgs>? BackupStateChanged;
    event EventHandler<BackupProgress>? BackupProgressChanged;
}

public class SchedulerService : ISchedulerService
{
    private readonly IConfigStore _config;
    private readonly IResticService _restic;
    private readonly INotificationService _notify;
    private readonly object _lock = new();
    private readonly HashSet<string> _running = new();
    private Timer? _timer;

    public bool IsPaused { get; set; }

    public event EventHandler<RunLogEntry>? RunCompleted;
    public event EventHandler<BackupStateChangedArgs>? BackupStateChanged;
    public event EventHandler<BackupProgress>? BackupProgressChanged;

    public SchedulerService(IConfigStore config, IResticService restic,
        INotificationService notify)
    {
        _config = config;
        _restic = restic;
        _notify = notify;
    }

    public void Start()
    {
        _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, TimeSpan.FromSeconds(60));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Tick()
    {
        if (IsPaused)
            return;
        var now = DateTime.Now;
        foreach (var job in _config.Config.Jobs.Where(j => j.Enabled))
        {
            var scheduledToday = now.Date + job.ScheduleTime;
            var alreadyRanToday = job.LastRunUtc is not null &&
                                  job.LastRunUtc.Value.ToLocalTime().Date == now.Date;
            if (!alreadyRanToday && now >= scheduledToday)
            {
                _ = RunJobAsync(job);
            }
        }
    }

    public async Task RunJobAsync(BackupJob job, bool manual = false, CancellationToken token = default)
    {
        lock (_lock)
        {
            if (_running.Contains(job.Id))
                return;
            _running.Add(job.Id);
        }

        try
        {
            BackupStateChanged?.Invoke(this, new BackupStateChangedArgs { IsRunning = true, JobName = job.Name });
            var repo = _config.Config.Repositories.FirstOrDefault(r => r.Id == job.RepositoryId);
            if (repo is null)
            {
                Finish(job, false, "Source not found.", TimeSpan.Zero, 0, manual, token);
                return;
            }

            var started = DateTime.UtcNow;
            var progress = new Progress<BackupProgress>(p =>
            {
                if (p.Error)
                    _notify.Show("Backup problem", p.Message, true);
                BackupProgressChanged?.Invoke(this, p);
            });

            var (success, bytesAdded) = await _restic.BackupAsync(repo, job, progress, token);
            var duration = DateTime.UtcNow - started;
            Finish(job, success, success ? "Backup completed." : "Backup failed.", duration, bytesAdded, manual, token);
        }
        catch (OperationCanceledException)
        {
            Finish(job, false, "Backup cancelled.", TimeSpan.Zero, 0, manual, token);
        }
        catch (Exception ex)
        {
            Finish(job, false, ex.Message, TimeSpan.Zero, 0, manual, token);
        }
        finally
        {
            lock (_lock)
            {
                _running.Remove(job.Id);
            }
            BackupStateChanged?.Invoke(this, new BackupStateChangedArgs { IsRunning = false, JobName = job.Name });
        }
    }

    private void Finish(BackupJob job, bool success, string detail, TimeSpan duration, long bytesAdded, bool manual, CancellationToken token)
    {
        job.LastRunUtc = DateTime.UtcNow;
        if (success)
            job.LastSuccessUtc = DateTime.UtcNow;
        _config.Save();

        var repo = _config.Config.Repositories.FirstOrDefault(r => r.Id == job.RepositoryId);
        var entry = new RunLogEntry
        {
            JobName = job.Name,
            RepositoryName = repo?.Name ?? "(unknown)",
            Success = success,
            Detail = detail,
            Duration = duration,
            BytesAdded = bytesAdded
        };
        RunCompleted?.Invoke(this, entry);

        if (_config.Config.Settings.NotificationsEnabled)
        {
            var (title, body) = BuildSyncMessage(job, repo, success, detail, manual);
            _notify.Show(title, body, !success);
        }
    }

    private static (string Title, string Body) BuildSyncMessage(
        BackupJob job, RepositoryConfig? repo, bool success, string detail, bool manual)
    {
        var repoName = repo?.Name ?? "the source";
        if (manual)
        {
            return success
                ? ("Manual sync complete",
                   $"Your files were backed up successfully to '{repoName}'.")
                : ("Manual sync failed",
                   $"The backup to '{repoName}' did not finish: {detail}");
        }

        return success
            ? ("Scheduled backup complete",
               $"'{job.Name}' was backed up successfully to '{repoName}'.")
            : ("Scheduled backup failed",
               $"'{job.Name}' could not be backed up: {detail}");
    }
}
