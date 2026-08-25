using System.Collections.ObjectModel;
using BackupManager.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BackupManager.ViewModels;

public partial class AppState : ObservableObject
{
    [ObservableProperty]
    private bool _isBackingUp;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _resticMissing;

    public ObservableCollection<RunLogEntry> RecentRuns { get; } = new();

    public event EventHandler? DataChanged;

    public void RaiseDataChanged() => DataChanged?.Invoke(this, EventArgs.Empty);
}
