using System.Collections.ObjectModel;
using System.Linq;
using BackupManager.Models;
using BackupManager.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupManager.ViewModels;

public partial class DashboardViewModel : ViewModelBase, IRefreshable
{
    private readonly IConfigStore _config;
    private readonly ISchedulerService _scheduler;
    private readonly INotificationService _notify;
    private readonly AppState _state;

    public event Action? RequestSnapshotsNavigation;

    public AppState State { get; }

    [ObservableProperty]
    private string _statusText = "Welcome to Backup Manager";

    [ObservableProperty]
    private string _lastBackupText = "No backups yet";

    [ObservableProperty]
    private bool _resticMissing;

    [ObservableProperty]
    private ObservableCollection<RunLogEntry> _recentRuns = new();

    public DashboardViewModel(IConfigStore config, ISchedulerService scheduler,
        INotificationService notify, AppState state)
    {
        _config = config;
        _scheduler = scheduler;
        _notify = notify;
        _state = state;
        State = state;
    }

    public void Refresh()
    {
        ResticMissing = _state.ResticMissing;
        var last = _config.Config.Jobs
            .Where(j => j.LastSuccessUtc is not null)
            .OrderByDescending(j => j.LastSuccessUtc)
            .FirstOrDefault();
        LastBackupText = last?.LastSuccessUtc is not null
            ? $"Last successful backup: {last.LastSuccessUtc.Value.ToLocalTime():g} ({last.Name})"
            : "No backups yet";

        StatusText = ResticMissing
            ? "restic not found — set its path in Settings."
            : "Everything is up to date.";

        RecentRuns = new ObservableCollection<RunLogEntry>(
            _state.RecentRuns.OrderByDescending(r => r.TimestampUtc).Take(20));
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        var job = _config.Config.Jobs.FirstOrDefault(j => j.Enabled);
        if (job is null)
        {
            _notify.Show("Nothing to sync", "Add a backup job in the Schedule tab first.", true);
            return;
        }
        await _scheduler.RunJobAsync(job);
        Refresh();
    }

    [RelayCommand]
    private void PullCopy()
    {
        RequestSnapshotsNavigation?.Invoke();
    }
}
