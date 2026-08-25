using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Platform.Storage;
using BackupManager.Models;
using BackupManager.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupManager.ViewModels;

public partial class SnapshotsViewModel : ViewModelBase, IRefreshable
{
    private readonly IConfigStore _config;
    private readonly IResticService _restic;
    private readonly INotificationService _notify;

    [ObservableProperty]
    private ObservableCollection<RepositoryConfig> _repositories = new();

    [ObservableProperty]
    private RepositoryConfig? _selectedRepository;

    [ObservableProperty]
    private ObservableCollection<SnapshotInfo> _snapshots = new();

    [ObservableProperty]
    private SnapshotInfo? _selectedSnapshot;

    [ObservableProperty]
    private ObservableCollection<string> _contents = new();

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private bool _busy;

    public SnapshotsViewModel(IConfigStore config, IResticService restic, INotificationService notify)
    {
        _config = config;
        _restic = restic;
        _notify = notify;
    }

    public void Refresh()
    {
        Repositories = new ObservableCollection<RepositoryConfig>(_config.Config.Repositories);
        Snapshots = new ObservableCollection<SnapshotInfo>();
        Contents = new ObservableCollection<string>();
        Status = Repositories.Count == 0 ? "Add a repository first." : "";
    }

    partial void OnSelectedRepositoryChanged(RepositoryConfig? value)
    {
        Snapshots = new ObservableCollection<SnapshotInfo>();
        Contents = new ObservableCollection<string>();
        if (value is not null)
            _ = LoadSnapshotsAsync();
    }

    partial void OnSelectedSnapshotChanged(SnapshotInfo? value)
    {
        Contents = new ObservableCollection<string>();
        if (value is not null && SelectedRepository is not null)
            _ = LoadContentsAsync(value);
    }

    [RelayCommand]
    private async Task LoadSnapshotsAsync()
    {
        if (SelectedRepository is null)
            return;
        Busy = true;
        Status = "Loading snapshots…";
        try
        {
            var list = await _restic.GetSnapshotsAsync(SelectedRepository);
            Snapshots = new ObservableCollection<SnapshotInfo>(
                list.OrderByDescending(s => s.Time));
            Status = Snapshots.Count == 0 ? "No snapshots yet." : $"{Snapshots.Count} snapshot(s).";
        }
        catch (System.Exception ex)
        {
            Status = "Error: " + ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task LoadContentsAsync(SnapshotInfo snapshot)
    {
        if (SelectedRepository is null)
            return;
        try
        {
            var files = await _restic.ListSnapshotContentsAsync(SelectedRepository, snapshot.Id, System.Threading.CancellationToken.None);
            Contents = new ObservableCollection<string>(files);
        }
        catch (System.Exception ex)
        {
            Status = "Could not list contents: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task PullCopyAsync()
    {
        if (SelectedRepository is null || SelectedSnapshot is null)
        {
            Status = "Select a repository and a snapshot.";
            return;
        }
        var window = App.MainWindowRef;
        if (window is null)
            return;

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose where to copy the backup",
            AllowMultiple = false
        });
        if (folders.Count == 0)
            return;

        var target = folders[0].Path.LocalPath;
        Busy = true;
        Status = $"Copying backup to {target}…";
        try
        {
            await _restic.RestoreAsync(SelectedRepository, SelectedSnapshot.Id, target, null, System.Threading.CancellationToken.None);
            Status = "Copy complete.";
            _notify.Show("Copy complete", $"Restored snapshot {SelectedSnapshot.ShortId} to {target}.", false);
        }
        catch (System.Exception ex)
        {
            Status = "Restore failed: " + ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }
}
