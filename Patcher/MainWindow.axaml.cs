using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Vantix.Patcher;

public partial class MainWindow : Window
{
    private readonly Updater _updater = new();

    public MainWindow()
    {
        InitializeComponent();
        TryLoadLogo();
        LoadChangelog();
        Opened += OnOpened;
    }

    private void TryLoadLogo()
    {
        try
        {
            using var s = AssetLoader.Open(new Uri("avares://VantixLauncher/Assets/logo.png"));
            Logo.Source = new Bitmap(s);
        }
        catch
        {
            Logo.IsVisible = false;
            TitleFallback.IsVisible = true;
        }
    }

    private void LoadChangelog()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
        Changelog.Markdown = File.Exists(path)
            ? File.ReadAllText(path)
            : "No changelog available.";
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        var status = new Progress<string>(t => Dispatcher.UIThread.Post(() => StatusText.Text = t));
        var percent = new Progress<int>(p => Dispatcher.UIThread.Post(() => Progress.Value = p));

        try
        {
            await _updater.CheckAndApplyAsync(status, percent);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }

        if (!GameLauncher.Exists)
        {
            StatusText.Text = "Game files not found.";
            return;
        }

        Progress.Value = 100;
        StatusText.Text = "Ready.";
        PlayButton.IsEnabled = true;
    }

    private void OnPlayClicked(object? sender, RoutedEventArgs e)
    {
        GameLauncher.Launch();
        Close();
    }
}
