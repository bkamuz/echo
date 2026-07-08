using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using echo.Core;
using echo.Abstractions.Core;

namespace echo.App.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly DictationCoordinator _coordinator;
    private readonly ModelDownloader _downloader;

    [ObservableProperty]
    private string _status = "Echo готов";

    public HomeViewModel(DictationCoordinator coordinator, ModelDownloader downloader)
    {
        _coordinator = coordinator;
        _downloader = downloader;
    }

    public string HotkeyHint => HotkeyTokens.ToDisplay(_coordinator.Config.Hotkey);

    public void NotifyConfigChanged() => OnPropertyChanged(nameof(HotkeyHint));

    [RelayCommand]
    private async Task DownloadGigaAmAsync()
    {
        Status = "Скачивание GigaAM v3…";
        var progress = new Progress<string>(s => Status = s);
        await _downloader.DownloadAsync(ModelRegistry.GigaAmSpec(), progress);
        Status = "GigaAM v3 готов";
    }

    [RelayCommand]
    private async Task DownloadWhisperAsync(string size)
    {
        Status = $"Скачивание Whisper {size}…";
        var progress = new Progress<string>(s => Status = s);
        await _downloader.DownloadAsync(ModelRegistry.WhisperSpec(size), progress);
        Status = $"Whisper {size} готов";
    }
}
