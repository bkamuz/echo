using CommunityToolkit.Mvvm.ComponentModel;
using echo.Abstractions.Core;
using echo.Core;

namespace echo.App.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly DictationCoordinator _coordinator;
    private readonly TranscriptionService _transcription;

    [ObservableProperty]
    private string _modelInfo = string.Empty;

    [ObservableProperty]
    private string _readinessText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLastOutcome))]
    private string _lastOutcome = string.Empty;

    public bool ShowLastOutcome => !string.IsNullOrWhiteSpace(LastOutcome);

    public HomeViewModel(DictationCoordinator coordinator, TranscriptionService transcription)
    {
        _coordinator = coordinator;
        _transcription = transcription;
        _coordinator.OutcomeChanged += OnOutcomeChanged;
        UpdateModelInfo();
    }

    public string HotkeyHint => HotkeyTokens.ToDisplay(_coordinator.Config.Hotkey);

    public void NotifyConfigChanged()
    {
        OnPropertyChanged(nameof(HotkeyHint));
        UpdateModelInfo();
    }

    private void OnOutcomeChanged()
    {
        LastOutcome = _coordinator.LastOutcomeMessage ?? string.Empty;
    }

    private void UpdateModelInfo()
    {
        try
        {
            ModelInfo = _transcription.Resolve(_coordinator.Config).DisplayName;
        }
        catch
        {
            ModelInfo = _coordinator.Config.Engine;
        }

        var spec = ModelRegistry.SpecForEngine(
            _coordinator.Config.Engine,
            _coordinator.Config.WhisperModelSize,
            _coordinator.Config.GigaAmModelSize);
        ReadinessText = spec is null
            ? "Модель не выбрана"
            : spec.IsDownloaded()
                ? "Готово к диктовке"
                : "Модель не загружена — скачайте в настройках или в мастере первого запуска";
    }
}
