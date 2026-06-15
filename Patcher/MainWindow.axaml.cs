using System;
using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Vantix.Patcher;

public partial class MainWindow : Window
{
    private const string ChangelogUrl = "https://raw.githubusercontent.com/justin-bobr/vantix/main/CHANGELOG.md";
    private static readonly HttpClient Http = new();

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

    private async void LoadChangelog()
    {
        try
        {
            Changelog.Markdown = await Http.GetStringAsync(ChangelogUrl);
        }
        catch
        {
            Changelog.Markdown = "No changelog available.";
        }
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
