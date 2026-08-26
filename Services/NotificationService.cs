using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BackupManager.Models;

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

    // Stable App User Model ID so Windows Action Center owns our toasts and groups
    // them under "Backup Manager" rather than an anonymous publisher.
    private const string AppId = "Olyxz.BackupManager";
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
        // Register the app so Action Center toasts are owned by "Backup Manager".
        RegisterWindows();

        // PowerShell ships the WinRT Windows.UI.Notifications types, so we delegate
        // toast creation to it — no compile-time dependency on the Windows SDK.
        var script =
            "$ErrorActionPreference='Stop';" +
            "$m=[Windows.UI.Notifications.ToastNotificationManager];" +
            "$t=$m::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02);" +
            "$x=$t.GetElementsByTagName('text');" +
            "$x.Item(0).AppendChild($t.CreateTextNode($args[0]));" +
            "if($x.Count -gt 1){$x.Item(1).AppendChild($t.CreateTextNode($args[1]))};" +
            "$m::CreateToastNotifier('" + AppId + "').Show([Windows.UI.Notifications.ToastNotification]::new($t));";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-EncodedCommand");
        psi.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        psi.ArgumentList.Add("-args");
        psi.ArgumentList.Add(title);
        psi.ArgumentList.Add(message);

        using var proc = Process.Start(psi);
        proc?.WaitForExit(5000);
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
        // Only set the process App User Model ID. Creating a Start Menu shortcut
        // with the AUMID used to be done here via raw COM PInvoke, but that call
        // could native-crash on some systems; the installer now owns the shortcut,
        // so we keep just this safe call.
        try { SetCurrentProcessExplicitAppUserModelID(AppId); } catch { }
    }

    [DllImport("shell32", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(string appId);
}
