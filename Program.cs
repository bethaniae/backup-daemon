using Avalonia;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace BackupManager;

sealed class Program
{
    static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BackupManager");
    static readonly string CrashLog = Path.Combine(LogDir, "crash.log");
    static readonly string TraceLog = Path.Combine(LogDir, "trace.log");

    static void LogCrash(string kind, object ex)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllText(CrashLog, $"[{DateTime.Now:u}] {kind}: {ex}\n");
        }
        catch { }
    }

    [STAThread]
    public static int Main(string[] args)
    {
        try { Directory.CreateDirectory(LogDir); } catch { }
        Trace.Listeners.Add(new TextWriterTraceListener(TraceLog));
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("Unhandled", e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
            LogCrash("UnobservedTask", e.Exception);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            LogCrash("Startup", ex);
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
