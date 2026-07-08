using CommunityToolkit.Mvvm.ComponentModel;
using echo.Core;

namespace echo.App.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly DictationCoordinator _coordinator;
    private readonly TranscriptionService _transcription;

    [ObservableProperty]
    private string _modelInfo = string.Empty;

    public HomeViewModel(DictationCoordinator coordinator, TranscriptionService transcription)
    {
        _coordinator = coordinator;
        _transcription = transcription;
        UpdateModelInfo();
    }

    public string HotkeyHint => HotkeyTokens.ToDisplay(_coordinator.Config.Hotkey);

    public void NotifyConfigChanged()
    {
        OnPropertyChanged(nameof(HotkeyHint));
        UpdateModelInfo();
    }

    private void UpdateModelInfo()
    {
        ModelInfo = _transcription.Resolve(_coordinator.Config).DisplayName;
    }
}
