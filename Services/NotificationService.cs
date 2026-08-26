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
        try { SetCurrentProcessExplicitAppUserModelID(AppId); } catch { }
        try { EnsureStartMenuShortcut(); } catch { }
    }

    private static void EnsureStartMenuShortcut()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return;
        var programs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
        Directory.CreateDirectory(programs);
        var lnk = Path.Combine(programs, "BackupManager.lnk");

        var link = (IShellLinkW)new ShellLink();
        link.SetPath(exe);
        link.SetWorkingDirectory(Path.GetDirectoryName(exe)!);
        link.SetDescription("Backup Manager");
        link.SetIconLocation(exe, 0);

        var store = (IPropertyStore)link;
        var pv = StringToPropVariant(AppId);
        var key = AppUserModelIdKey;
        store.SetValue(ref key, ref pv);
        store.Commit();
        if (pv.data != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(pv.data);
            pv.data = IntPtr.Zero;
        }

        ((IPersistFile)link).Save(lnk, true);
    }

    private static PropVariant StringToPropVariant(string value) => new()
    {
        vt = 31, // VT_LPWSTR
        data = Marshal.StringToCoTaskMemUni(value)
    };

    [DllImport("shell32", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(string appId);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046"), ClassInterface(ClassInterfaceType.None)]
    private class ShellLink { }

    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIcon, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIcon, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("0000010B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [ComImport, Guid("886D8EEB-186F-4C41-9C11-11C1D7B7F2F2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out int cProps);
        void GetAt(int iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, ref PropVariant pv);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public int pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public IntPtr data;
        public IntPtr data2;
    }

    private static readonly PropertyKey AppUserModelIdKey = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 5
    };
}
