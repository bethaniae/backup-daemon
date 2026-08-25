using Avalonia;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace BackupManager;

sealed class Program
{
    const string CrashLog = "/tmp/backupmanager_crash.log";
    const string TraceLog = "/tmp/backupmanager_trace.log";

    [STAThread]
    public static int Main(string[] args)
    {
        Trace.Listeners.Add(new TextWriterTraceListener(TraceLog));
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            File.AppendAllText(CrashLog, $"[{DateTime.Now}] Unhandled: {e.ExceptionObject}\n");
        TaskScheduler.UnobservedTaskException += (_, e) =>
            File.AppendAllText(CrashLog, $"[{DateTime.Now}] UnobservedTask: {e.Exception}\n");

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            File.AppendAllText(CrashLog, $"[{DateTime.Now}] Startup: {ex}\n");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .WithInterFont()
            .LogToTrace();

        var backend = Environment.GetEnvironmentVariable("BM_BACKEND")?.ToLowerInvariant();
        if (backend == "x11")
            builder = builder.UseX11().UseSkia().UseHarfBuzz();
        else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            builder = builder.UseX11().UseSkia().UseHarfBuzz();
        else
            builder = builder.UsePlatformDetect();

#if DEBUG
        builder = builder.WithDeveloperTools();
#endif
        return builder;
    }
}
