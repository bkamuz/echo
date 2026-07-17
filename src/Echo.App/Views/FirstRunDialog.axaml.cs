using Avalonia.Controls;
using Avalonia.Interactivity;
using echo.Abstractions.Core;
using echo.App.Localization;
using echo.App.ViewModels;
using echo.Core;
using Microsoft.Extensions.DependencyInjection;

namespace echo.App.Views;

public partial class FirstRunDialog : Window
{
    private readonly SettingsViewModel _settings;
    private readonly LocalizationService _loc;

    public FirstRunDialog()
    {
        InitializeComponent();
        _settings = null!;
        _loc = null!;
    }

    public FirstRunDialog(SettingsViewModel settings, DictationCoordinator coordinator)
    {
        InitializeComponent();
        _settings = settings;
        _loc = App.Services.GetRequiredService<LocalizationService>();
        DataContext = new FirstRunViewModel(coordinator, _loc);
    }

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        DownloadButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        StatusText.Text = _loc.Get("Loc.FirstRun.Downloading");
        try
        {
            if (_settings.DownloadModelCommand.CanExecute(null))
            {
                await _settings.DownloadModelCommand.ExecuteAsync(null);
            }

            StatusText.Text = _loc.Get("Loc.Status.Done");
            Close(true);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            DownloadButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
        }
    }

    private void OnLaterClick(object? sender, RoutedEventArgs e) => Close(false);
}

public sealed class FirstRunViewModel(DictationCoordinator coordinator, LocalizationService loc)
{
    public string ModelTitle =>
        ModelRegistry.SpecForEngine(
            coordinator.Config.Engine,
            coordinator.Config.WhisperModelSize,
            coordinator.Config.GigaAmModelSize)?.Title
        ?? loc.Get("Loc.Model.DefaultTitle");

    public string HotkeyHint =>
        loc.Format(
            "Loc.FirstRun.HotkeyHint",
            HotkeyTokens.ToDisplay(coordinator.Config.Hotkey));
}
