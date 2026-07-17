using CommunityToolkit.Mvvm.ComponentModel;
using echo.Abstractions.Core;
using echo.App.Localization;
using echo.Core;

namespace echo.App.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly DictationCoordinator _coordinator;
    private readonly TranscriptionService _transcription;
    private readonly LocalizationService _loc;

    [ObservableProperty]
    private string _modelInfo = string.Empty;

    [ObservableProperty]
    private string _readinessText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLastOutcome))]
    private string _lastOutcome = string.Empty;

    public bool ShowLastOutcome => !string.IsNullOrWhiteSpace(LastOutcome);

    public HomeViewModel(
        DictationCoordinator coordinator,
        TranscriptionService transcription,
        LocalizationService loc)
    {
        _coordinator = coordinator;
        _transcription = transcription;
        _loc = loc;
        _coordinator.OutcomeChanged += OnOutcomeChanged;
        _loc.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HotkeyHint));
            UpdateModelInfo();
        };
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
        var raw = _coordinator.LastOutcomeMessage ?? string.Empty;
        LastOutcome = _loc.LocText(raw);
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
            ? _loc.Get("Loc.Home.ModelNotSelected")
            : spec.IsDownloaded()
                ? _loc.Get("Loc.Home.Ready")
                : _loc.Get("Loc.Home.ModelMissing");
    }
}
