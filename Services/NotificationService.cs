using System;
using System.Diagnostics;
using System.IO;
using BackupManager.Models;

#if WINDOWS
using Microsoft.Toolkit.Uwp.Notifications;
using System.Runtime.InteropServices;
#endif

namespace BackupManager.Services;

public class NotificationEventArgs : EventArgs
{
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public bool IsError { get; init; }
}

public interface INotificationService
{
    event EventHandler<NotificationEventArgs>? NotificationRequested;
    void Show(string title, string message, bool isError = false);
    void Register();
}

public class NotificationService : INotificationService
{
    public event EventHandler<NotificationEventArgs>? NotificationRequested;

    private static bool _registered;

    public void Show(string title, string message, bool isError = false)
    {
        // Keep the in-process event for any UI subscribers.
        NotificationRequested?.Invoke(this, new NotificationEventArgs
        {
            Title = title,
            Message = message,
            IsError = isError
        });

        try
        {
            if (OperatingSystem.IsWindows())
                ShowWindows(title, message);
            else if (OperatingSystem.IsLinux())
                ShowLinux(title, message);
        }
        catch
        {
            // Notifications are best-effort; never let them break the app.
        }
    }

    private static void ShowWindows(string title, string message)
    {
#if WINDOWS
        // ToastNotificationManagerCompat (WCT 7.1.2) handles Win32 unpackaged apps
        // without requiring a Start Menu shortcut or AUMID registration. The toast
        // simply shows via the Windows Action Center.
        new ToastContentBuilder()
            .AddArgument("action", "syncComplete")
            .AddText(title)
            .AddText(message)
            .Show();
#else
        // No Windows notification backend in this build target; Linux never calls this.
#endif
    }

    private static void ShowLinux(string title, string message)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "notify-send",
            ArgumentList = { "--app-name", "Backup Manager", title, message },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = Process.Start(psi);
        proc?.WaitForExit(4000);
    }

    public void Register() => RegisterWindows();

    private static void RegisterWindows()
    {
        if (!OperatingSystem.IsWindows() || _registered)
            return;
        _registered = true;
#if WINDOWS
        // WCT 7.1.2 requires no AUMID/Start-Menu-shortcut registration for Win32 apps;
        // toasts show directly via the Action Center. Nothing to initialize here.
#endif
    }

    private static void LogNotify(string message)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BackupManager");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "notify.log"), $"[{DateTime.Now:u}] {message}\n");
        }
        catch { }
    }
}
