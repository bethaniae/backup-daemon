using System.Collections.ObjectModel;
using System.Linq;
using BackupManager.Models;
using BackupManager.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BackupManager.ViewModels;

public partial class LogsViewModel : ViewModelBase, IRefreshable
{
    private readonly AppState _state;

    [ObservableProperty]
    private ObservableCollection<RunLogEntry> _runs = new();

    public LogsViewModel(AppState state)
    {
        _state = state;
        _state.DataChanged += (_, _) => Refresh();
    }

    public void Refresh()
    {
        Runs = new ObservableCollection<RunLogEntry>(
            _state.RecentRuns.OrderByDescending(r => r.TimestampUtc));
    }
}
