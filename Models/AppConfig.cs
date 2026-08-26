namespace BackupManager.Models;

public class RepositoryConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "My Repository";
    public string LocalPath { get; set; } = "";
    public string Location { get; set; } = "";
    public string Password { get; set; } = "";
    public string? Notes { get; set; }
}

public class BackupJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Daily Backup";
    public string RepositoryId { get; set; } = "";
    public List<string> Excludes { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public TimeSpan ScheduleTime { get; set; } = new(8, 30, 0);
    public DateTime? LastRunUtc { get; set; }
    public DateTime? LastSuccessUtc { get; set; }
}

public class AppSettings
{
    public string ResticPath { get; set; } = "restic";
    public bool StartWithOs { get; set; }
    public bool StartHidden { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
    public string? DownloadFolder { get; set; }
}

public class AppConfig
{
    public List<RepositoryConfig> Repositories { get; set; } = new();
    public List<BackupJob> Jobs { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
    public DateTime? LastVersionCheckUtc { get; set; }
}
