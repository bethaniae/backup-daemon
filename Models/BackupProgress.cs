namespace BackupManager.Models;

public class BackupProgress
{
    public double Percent { get; set; }
    public long FilesDone { get; set; }
    public long FilesTotal { get; set; }
    public long BytesDone { get; set; }
    public long BytesTotal { get; set; }
    public string? CurrentFile { get; set; }
    public string Message { get; set; } = "";
    public bool Finished { get; set; }
    public bool Error { get; set; }
}

public class RunLogEntry
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string JobName { get; set; } = "";
    public string RepositoryName { get; set; } = "";
    public bool Success { get; set; }
    public string Detail { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public long BytesAdded { get; set; }
}
