using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using BackupManager.Services;
using BackupManager.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace BackupManager.ViewModels;

public class NavItem
{
    public string Name { get; init; } = "";
    public string Icon { get; init; } = "";
    public System.Type ViewModelType { get; init; } = null!;
}

public partial class MainViewModel : ObservableObject
{
    private readonly IConfigStore _config;
    private readonly ISchedulerService _scheduler;
    private readonly IResticService _restic;
    private readonly INotificationService _notify;
    private readonly AppState _state;

    [ObservableProperty]
    private NavItem? _selectedNav;

    [ObservableProperty]
    private ViewModelBase? _currentView;

    [ObservableProperty]
    private bool _isSchedulePaused;

    [ObservableProperty]
    private bool _startHidden;

    [ObservableProperty]
    private string _trayTooltip = "Backup Manager";

    [ObservableProperty]
    private bool _duckVisible;

    public ObservableCollection<NavItem> NavItems { get; } = new();

    public bool ForceClose { get; set; }

    public MainViewModel(IConfigStore config, ISchedulerService scheduler,
        IResticService restic, INotificationService notify, AppState state)
    {
        _config = config;
        _scheduler = scheduler;
        _restic = restic;
        _notify = notify;
        _state = state;
        _startHidden = config.Config.Settings.StartHidden;
        _isSchedulePaused = scheduler.IsPaused;

        NavItems.Add(new NavItem { Name = "Dashboard", Icon = "🏠", ViewModelType = typeof(DashboardViewModel) });
        NavItems.Add(new NavItem { Name = "Repositories", Icon = "🗄️", ViewModelType = typeof(RepositoriesViewModel) });
        NavItems.Add(new NavItem { Name = "Snapshots", Icon = "📸", ViewModelType = typeof(SnapshotsViewModel) });
        NavItems.Add(new NavItem { Name = "Schedule", Icon = "🕒", ViewModelType = typeof(ScheduleViewModel) });
        NavItems.Add(new NavItem { Name = "Logs", Icon = "📜", ViewModelType = typeof(LogsViewModel) });
        NavItems.Add(new NavItem { Name = "Settings", Icon = "⚙️", ViewModelType = typeof(SettingsViewModel) });

        SelectedNav = NavItems[0];

        _scheduler.BackupStateChanged += (_, e) =>
        {
            _state.IsBackingUp = e.IsRunning;
            _state.BackupJobName = e.IsRunning ? e.JobName : "";
            if (!e.IsRunning)
            {
                _state.BackupPercent = 0;
                _state.BackupDetail = "";
                _state.BackupIndeterminate = false;
            }
            TrayTooltip = e.IsRunning ? $"Backing up: {e.JobName}" : "Backup Manager";
        };
        _scheduler.BackupProgressChanged += (_, p) =>
        {
            if (p.Finished)
                return;
            _state.BackupIndeterminate = p.BytesTotal <= 0 || p.Percent <= 0;
            _state.BackupPercent = p.Percent;
            var parts = new List<string>();
            if (p.FilesTotal > 0)
                parts.Add($"{p.FilesDone:N0} / {p.FilesTotal:N0} files");
            if (!string.IsNullOrWhiteSpace(p.CurrentFile))
                parts.Add(Path.GetFileName(p.CurrentFile));
            _state.BackupDetail = parts.Count > 0 ? string.Join(" — ", parts) : "Preparing backup…";
        };
        _scheduler.RunCompleted += (_, e) => _state.RaiseDataChanged();
        _notify.NotificationRequested += (_, e) => { };

        _duckVisible = _config.Config.Settings.ActivateDucky;
        _config.SettingsChanged += (_, _) => DuckVisible = _config.Config.Settings.ActivateDucky;

        _ = CheckResticAsync();
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null)
            return;
        var vm = (ViewModelBase)App.Services.GetRequiredService(value.ViewModelType);
        if (vm is IRefreshable r)
            r.Refresh();
        if (vm is DashboardViewModel dashboard)
            dashboard.RequestSnapshotsNavigation += () =>
                SelectedNav = NavItems.First(n => n.ViewModelType == typeof(SnapshotsViewModel));
        CurrentView = vm;
    }

    partial void OnStartHiddenChanged(bool value)
    {
        _config.Config.Settings.StartHidden = value;
        _config.Save();
    }

    partial void OnIsSchedulePausedChanged(bool value)
    {
        _scheduler.IsPaused = value;
    }

    private async Task CheckResticAsync()
    {
        try
        {
            await _restic.GetVersionAsync();
            _state.ResticMissing = false;
        }
        catch
        {
            _state.ResticMissing = true;
            _state.StatusMessage = "restic not found. Set its path in Settings.";
        }
        _state.RaiseDataChanged();
    }

    [RelayCommand]
    private void ShowWindow()
    {
        var w = App.MainWindowRef;
        if (w is null)
            return;
        w.Show();
        w.WindowState = WindowState.Normal;
        w.Activate();
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
        await _scheduler.RunJobAsync(job, manual: true);
    }

    [RelayCommand]
    private void TogglePause()
    {
        IsSchedulePaused = !IsSchedulePaused;
        _scheduler.IsPaused = IsSchedulePaused;
    }

    [RelayCommand]
    private void Exit()
    {
        ForceClose = true;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
