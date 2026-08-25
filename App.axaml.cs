using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BackupManager.Services;
using BackupManager.ViewModels;
using BackupManager.Views;
using Microsoft.Extensions.DependencyInjection;

namespace BackupManager;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static MainWindow? MainWindowRef { get; set; }
    public static bool TrayAvailable { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Log("start");
        var services = new ServiceCollection();
        services.AddSingleton<IConfigStore, JsonConfigStore>();
        services.AddSingleton<IResticService, ResticService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<ISchedulerService, SchedulerService>();
        services.AddSingleton<AppState>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<RepositoriesViewModel>();
        services.AddTransient<SnapshotsViewModel>();
        services.AddTransient<ScheduleViewModel>();
        services.AddTransient<LogsViewModel>();
        services.AddTransient<SettingsViewModel>();
        Services = services.BuildServiceProvider();
        Log("services built");

        var config = Services.GetRequiredService<IConfigStore>();
        config.Load();
        Log("config loaded");

        var main = Services.GetRequiredService<MainViewModel>();
        DataContext = main;
        Log("main resolved");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow { DataContext = main };
            window.Loaded += (_, _) => Log("WINDOW LOADED");
            MainWindowRef = window;
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, e) =>
            {
                if (!main.ForceClose && main.CloseToTray && TrayAvailable && window.IsVisible)
                {
                    e.Cancel = true;
                    window.Hide();
                }
            };
            Log("window assigned");
        }
        else
        {
            Log("NO desktop lifetime");
        }

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BM_NOTRAY")))
            TryCreateTrayIcon(main);
        Log("tray attempted");

        var scheduler = Services.GetRequiredService<ISchedulerService>();
        scheduler.Start();
        Log("scheduler started");

        base.OnFrameworkInitializationCompleted();
        Log("base done; showing window explicitly");
        MainWindowRef?.Show();
    }

    private static void Log(string msg)
    {
        try { File.AppendAllText("/tmp/backupmanager_trace.log", $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); }
        catch { }
    }

    private void TryCreateTrayIcon(MainViewModel main)
    {
        try
        {
            var menu = new NativeMenu();
            menu.Add(new NativeMenuItem { Header = "Open Dashboard", Command = main.ShowWindowCommand });
            menu.Add(new NativeMenuItem { Header = "Sync new data now", Command = main.SyncNowCommand });
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(new NativeMenuItem { Header = "Pause / resume schedule", Command = main.TogglePauseCommand });
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(new NativeMenuItem { Header = "Quit", Command = main.ExitCommand });

            var iconUri = new Uri("avares://BackupManager/Assets/avalonia-logo.ico", UriKind.Absolute);
            using var iconStream = Avalonia.Platform.AssetLoader.Open(iconUri);
            var tray = new TrayIcon
            {
                Icon = new WindowIcon(iconStream),
                ToolTipText = "Backup Manager",
                Menu = menu,
                Command = main.ShowWindowCommand
            };
            var icons = new TrayIcons();
            icons.Add(tray);
            SetValue(TrayIcon.IconsProperty, icons);
            TrayAvailable = true;
        }
        catch
        {
            // Tray is optional (e.g. some Wayland compositors). The window stays the primary surface.
        }
    }
}
