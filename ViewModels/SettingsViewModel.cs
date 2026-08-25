using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Platform.Storage;
using BackupManager.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupManager.ViewModels;

public partial class SettingsViewModel : ViewModelBase, IRefreshable
{
    private readonly IConfigStore _config;
    private readonly IResticService _restic;
    private readonly INotificationService _notify;

    [ObservableProperty]
    private string _resticPath = "restic";

    [ObservableProperty]
    private bool _closeToTray = true;

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private bool _startWithOs;

    [ObservableProperty]
    private string _downloadFolder = "";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _resticVersion = "";

    public SettingsViewModel(IConfigStore config, IResticService restic, INotificationService notify)
    {
        _config = config;
        _restic = restic;
        _notify = notify;
    }

    public void Refresh()
    {
        var s = _config.Config.Settings;
        ResticPath = s.ResticPath;
        CloseToTray = s.CloseToTray;
        NotificationsEnabled = s.NotificationsEnabled;
        StartWithOs = s.StartWithOs;
        DownloadFolder = s.DownloadFolder ?? "";
        _ = LoadVersionAsync();
    }

    private async System.Threading.Tasks.Task LoadVersionAsync()
    {
        try
        {
            ResticVersion = await _restic.GetVersionAsync();
        }
        catch (Exception ex)
        {
            ResticVersion = "Not found: " + ex.Message;
        }
    }

    [RelayCommand]
    private void Save()
    {
        var s = _config.Config.Settings;
        s.ResticPath = string.IsNullOrWhiteSpace(ResticPath) ? "restic" : ResticPath;
        s.CloseToTray = CloseToTray;
        s.NotificationsEnabled = NotificationsEnabled;
        s.StartWithOs = StartWithOs;
        s.DownloadFolder = DownloadFolder;
        _config.Save();
        ApplyAutostart();
        Status = "Settings saved.";
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        Status = "Checking…";
        try
        {
            ResticVersion = await _restic.GetVersionAsync();
            Status = "restic works.";
        }
        catch (Exception ex)
        {
            ResticVersion = "";
            Status = "restic not found: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task PickFolderAsync()
    {
        var window = App.MainWindowRef;
        if (window is null)
            return;
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Default download folder for pulled copies",
            AllowMultiple = false
        });
        if (folders.Count > 0)
            DownloadFolder = folders[0].Path.LocalPath;
    }

    private void ApplyAutostart()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var autostart = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "autostart");
                Directory.CreateDirectory(autostart);
                var desktopFile = Path.Combine(autostart, "BackupManager.desktop");
                if (StartWithOs)
                {
                    var exe = Environment.ProcessPath ?? "";
                    File.WriteAllText(desktopFile,
$"[Desktop Entry]\nType=Application\nName=Backup Manager\nExec={exe}\nHidden=false\nX-GNOME-Autostart-enabled=true\n");
                }
                else if (File.Exists(desktopFile))
                {
                    File.Delete(desktopFile);
                }
            }
        }
        catch
        {
            // Autostart is best-effort; ignore failures.
        }
    }
}
