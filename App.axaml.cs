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

        var config = Services.GetRequiredService<IConfigStore>();
        config.Load();

        var main = Services.GetRequiredService<MainViewModel>();
        DataContext = main;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var window = new MainWindow { DataContext = main };
            MainWindowRef = window;
            desktop.MainWindow = window;
            window.Closing += (_, e) =>
            {
                // The GUI is a detachable view. Unless an explicit quit was requested,
                // closing it returns to the tray instead of terminating the app.
                if (main.ForceClose)
                    return;
                if (!TrayAvailable)
                {
                    // No tray to return to: quitting is the only sane option.
                    main.ForceClose = true;
                    desktop.Shutdown();
                    return;
                }
                e.Cancel = true;
                window.Hide();
            };
        }

        TryCreateTrayIcon(main);

        var scheduler = Services.GetRequiredService<ISchedulerService>();
        scheduler.Start();

        if (OperatingSystem.IsWindows())
        {
            try { Services.GetRequiredService<INotificationService>().Register(); }
            catch { }
        }

        base.OnFrameworkInitializationCompleted();

        // The tray icon is the persistent host. The window is shown on startup only
        // when there is no tray to interact with, or the user opted to show it.
        if (!TrayAvailable || !main.StartHidden)
            MainWindowRef?.Show();
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
