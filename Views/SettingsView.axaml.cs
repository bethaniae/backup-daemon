using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BackupManager.Views;

public partial class SettingsView : UserControl
{
    private const string DuckyArtUrl = "https://caz-bee.itch.io/ducky-3";

    public SettingsView()
    {
        InitializeComponent();
    }

    private async void OnDuckyCreditClick(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
            return;
        await top.Launcher.LaunchUriAsync(new Uri(DuckyArtUrl));
    }
}