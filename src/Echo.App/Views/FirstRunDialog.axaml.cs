using Avalonia.Controls;
using Avalonia.Interactivity;
using echo.Abstractions.Core;
using echo.App.ViewModels;
using echo.Core;

namespace echo.App.Views;

public partial class FirstRunDialog : Window
{
    private readonly SettingsViewModel _settings;

    public FirstRunDialog()
    {
        InitializeComponent();
        _settings = null!;
    }

    public FirstRunDialog(SettingsViewModel settings, DictationCoordinator coordinator)
    {
        InitializeComponent();
        _settings = settings;
        DataContext = new FirstRunViewModel(coordinator);
    }

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        DownloadButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        StatusText.Text = "Скачивание…";
        try
        {
            if (_settings.DownloadModelCommand.CanExecute(null))
            {
                await _settings.DownloadModelCommand.ExecuteAsync(null);
            }

            StatusText.Text = "Готово";
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

public sealed class FirstRunViewModel(DictationCoordinator coordinator)
{
    public string ModelTitle =>
        ModelRegistry.SpecForEngine(
            coordinator.Config.Engine,
            coordinator.Config.WhisperModelSize,
            coordinator.Config.GigaAmModelSize)?.Title
        ?? "Модель распознавания";

    public string HotkeyHint =>
        $"Горячая клавиша: {HotkeyTokens.ToDisplay(coordinator.Config.Hotkey)} — удерживайте и говорите.";
}
