using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using echo.App.Localization;
using echo.App.Services;
using echo.Core;
using echo.Platform.Linux;
using echo.Platform.Windows;
using echo.Abstractions.Core;
using echo.Abstractions.Engines;
using echo.Abstractions.Platform;

namespace echo.App.ViewModels;

public sealed record TypeSpeedOption(string Label, int DelayMs)
{
    public override string ToString() => Label;
}

public sealed record UiLanguageChoice(string Code, string Label)
{
    public override string ToString() => Label;
}

public partial class SettingsViewModel : ObservableObject
{
    private static readonly string[] AllEngineIds = ["gigaam", "whisper", "omnilingual"];

    private readonly DictationCoordinator _coordinator;
    private readonly IAudioCapture _audio;
    private readonly HomeViewModel _home;
    private readonly AppStatusViewModel _status;
    private readonly SettingsApplyService _applyService;
    private readonly IDirectMlAvailability _directMlAvailability;
    private readonly IAutoStartService _autoStartService;
    private readonly HotkeyCaptureController _hotkeyCapture;
    private readonly ModelSettingsController _models;
    private readonly LocalizationService _loc;
    private readonly DirectMlRuntimeInstaller? _directMlInstaller;
    private readonly HashSet<string> _registeredEngineIds;
    private bool _isLoadingFromConfig;

    [ObservableProperty] private string _hotkey = string.Empty;
    [ObservableProperty] private string _engine = string.Empty;
    [ObservableProperty] private string _whisperModelSize = string.Empty;
    [ObservableProperty] private string _gigaAmModelSize = string.Empty;
    [ObservableProperty] private string _language = string.Empty;
    [ObservableProperty] private ComputeDeviceOption? _selectedComputeDevice;
    [ObservableProperty] private AudioDeviceInfo? _inputDevice;
    [ObservableProperty] private InputMethodOption? _selectedInputMethod;
    [ObservableProperty] private TypeSpeedOption? _selectedTypeSpeed;
    [ObservableProperty] private UiLanguageChoice? _selectedUiLanguage;
    [ObservableProperty] private bool _addTrailingSpace;
    [ObservableProperty] private bool _showDictationToast;
    [ObservableProperty] private bool _startWithSystem;
    [ObservableProperty] private bool _isCapturingHotkey;
    [ObservableProperty] private string _hotkeyPreview = string.Empty;
    [ObservableProperty] private string _modelStatus = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowModelDownloadButton))]
    private bool _isModelDownloaded;
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ShowModelDownloadButton),
        nameof(ModelLoadedTooltip),
        nameof(ModelDownloadTooltip),
        nameof(ModelDeleteTooltip))]
    private bool _hasCurrentModel;
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ModelLoadedTooltip),
        nameof(ModelDownloadTooltip),
        nameof(ModelDeleteTooltip))]
    private string _modelTitle = string.Empty;
    [ObservableProperty] private bool _isApplying;

    public bool ShowModelDownloadButton => HasCurrentModel && !IsModelDownloaded;

    public string ModelLoadedTooltip =>
        HasCurrentModel ? _loc.Format("Loc.Settings.ModelLoaded.Tooltip", ModelTitle) : string.Empty;

    public string ModelDownloadTooltip =>
        HasCurrentModel ? _loc.Format("Loc.Settings.ModelDownload.Tooltip", ModelTitle) : string.Empty;

    public string ModelDeleteTooltip =>
        HasCurrentModel ? _loc.Format("Loc.Settings.ModelDelete.Tooltip", ModelTitle) : string.Empty;

    public SettingsViewModel(
        DictationCoordinator coordinator,
        IAudioCapture audio,
        HomeViewModel home,
        AppStatusViewModel status,
        SettingsApplyService applyService,
        IDirectMlAvailability directMlAvailability,
        IAutoStartService autoStartService,
        HotkeyCaptureController hotkeyCapture,
        ModelSettingsController models,
        IEnumerable<ITranscriptionEngine> engines,
        LocalizationService loc,
        DirectMlRuntimeInstaller? directMlInstaller = null)
    {
        _coordinator = coordinator;
        _audio = audio;
        _home = home;
        _status = status;
        _applyService = applyService;
        _directMlAvailability = directMlAvailability;
        _autoStartService = autoStartService;
        _hotkeyCapture = hotkeyCapture;
        _models = models;
        _loc = loc;
        _directMlInstaller = directMlInstaller;

        _registeredEngineIds = engines.Select(e => e.EngineId).ToHashSet(StringComparer.Ordinal);
        RebuildEngineOptions();
        RebuildTypeSpeedOptions();
        RebuildUiLanguageChoices();

        _loc.LanguageChanged += (_, _) => RefreshLocalizedOptions();

        LoadFromConfig();
    }

    public bool IsAutoStartSupported => _autoStartService.IsSupported;

    private ComputeDeviceOption CpuDeviceOption => new(
        ExecutionProviderResolver.CpuDevice,
        _loc.Get("Loc.Device.Cpu"),
        _loc.Get("Loc.Device.Cpu.Tooltip"));

    private ComputeDeviceOption DirectMlDeviceOption => new(
        ExecutionProviderResolver.DirectMlDevice,
        _loc.Get("Loc.Device.DirectMl"),
        _loc.Get("Loc.Device.DirectMl.Tooltip"));

    public IReadOnlyList<EngineOption> EngineOptions { get; private set; } = [];
    public IReadOnlyList<ComputeDeviceOption> ComputeDeviceOptions => BuildComputeDeviceOptions();
    public IReadOnlyList<TypeSpeedOption> TypeSpeedOptions { get; private set; } = [];
    public IReadOnlyList<UiLanguageChoice> UiLanguageChoices { get; private set; } = [];

    private IReadOnlyList<ComputeDeviceOption> BuildComputeDeviceOptions()
    {
        if (IsWhisper)
        {
            return [CpuDeviceOption];
        }

        var dmlAvailable = _directMlAvailability.IsAvailable;
        return dmlAvailable
            ? [CpuDeviceOption, DirectMlDeviceOption]
            : [CpuDeviceOption];
    }

    public IReadOnlyList<string> WhisperSizes => AppConfig.WhisperSizes;
    public IReadOnlyList<string> GigaAmSizes => AppConfig.GigaAmSizes;
    public IReadOnlyList<InputMethodOption> InputMethodOptions =>
        OperatingSystem.IsLinux()
            ? BuildLinuxInputMethodOptions()
            :
            [
                new("clipboard", _loc.Get("Loc.Input.Clipboard"), _loc.Get("Loc.Input.Clipboard.Tooltip")),
                new("type", _loc.Get("Loc.Input.Type"), _loc.Get("Loc.Input.Type.Tooltip")),
            ];

    private IReadOnlyList<InputMethodOption> BuildLinuxInputMethodOptions()
    {
        if (LinuxDependencyCatalog.UsesGnomeWaylandYdotool)
        {
            return
            [
                new(
                    "auto",
                    _loc.Get("Loc.Input.Auto"),
                    _loc.Get("Loc.Input.Linux.Auto.Gnome.Tooltip")),
                new(
                    "clipboard",
                    _loc.Get("Loc.Input.Clipboard"),
                    _loc.Get("Loc.Input.Linux.Clipboard.Gnome.Tooltip")),
            ];
        }

        return
        [
            new(
                "auto",
                _loc.Get("Loc.Input.Auto"),
                _loc.Get("Loc.Input.Linux.Auto.Tooltip")),
            new(
                "clipboard",
                _loc.Get("Loc.Input.Clipboard"),
                _loc.Get("Loc.Input.Linux.Clipboard.Tooltip")),
        ];
    }

    public IReadOnlyList<string> Languages { get; } = ["auto", "ru", "en"];

    public bool IsTypeInput =>
        !OperatingSystem.IsLinux() && SelectedInputMethod?.Id == "type";

    public string TypeSpeedTooltip => _loc.Get("Loc.Settings.TypeSpeed.Tooltip");

    public string HotkeyDisplay =>
        IsCapturingHotkey && !string.IsNullOrEmpty(HotkeyPreview)
            ? HotkeyPreview
            : HotkeyTokens.ToDisplay(Hotkey);

    public bool IsSettingsEnabled => !IsApplying;

    public IReadOnlyList<AudioDeviceInfo> InputDevices => _audio.ListInputDevices();

    public bool IsGigaAm => Engine == "gigaam";
    public bool IsWhisper => Engine == "whisper";
    public bool IsOmnilingual => Engine == "omnilingual";
    public bool IsDeviceVisible => Engine != "whisper";

    public EngineOption? SelectedEngine
    {
        get => EngineOptions.FirstOrDefault(e => e.Id == Engine);
        set
        {
            if (value is not null && value.Id != Engine)
            {
                Engine = value.Id;
            }
        }
    }

    partial void OnIsApplyingChanged(bool value) => OnPropertyChanged(nameof(IsSettingsEnabled));

    partial void OnEngineChanged(string value)
    {
        OnPropertyChanged(nameof(IsGigaAm));
        OnPropertyChanged(nameof(IsWhisper));
        OnPropertyChanged(nameof(IsOmnilingual));
        OnPropertyChanged(nameof(IsDeviceVisible));
        OnPropertyChanged(nameof(SelectedEngine));
        OnPropertyChanged(nameof(ComputeDeviceOptions));
        EnsureValidComputeDevice();
        if (value == "whisper" && Language == "ru")
        {
            _isLoadingFromConfig = true;
            try
            {
                Language = "auto";
            }
            finally
            {
                _isLoadingFromConfig = false;
            }
        }
        UpdateModelStatus();
        ScheduleApply();
    }

    partial void OnGigaAmModelSizeChanged(string value)
    {
        UpdateModelStatus();
        ScheduleApply();
    }

    partial void OnWhisperModelSizeChanged(string value)
    {
        UpdateModelStatus();
        ScheduleApply();
    }

    partial void OnLanguageChanged(string value) => ScheduleApply();

    partial void OnSelectedComputeDeviceChanged(ComputeDeviceOption? value)
    {
        if (_isLoadingFromConfig)
        {
            return;
        }

        if (value?.Id == ExecutionProviderResolver.DirectMlDevice && _directMlInstaller is not null)
        {
            _ = EnsureDirectMlThenApplyAsync();
            return;
        }

        ScheduleApply();
    }

    partial void OnSelectedInputMethodChanged(InputMethodOption? value)
    {
        OnPropertyChanged(nameof(IsTypeInput));
        ScheduleApply();
    }

    partial void OnSelectedTypeSpeedChanged(TypeSpeedOption? value) => ScheduleApply();

    partial void OnSelectedUiLanguageChanged(UiLanguageChoice? value)
    {
        if (_isLoadingFromConfig || value is null)
        {
            return;
        }

        _loc.Apply(value.Code);
        ScheduleApply();
    }

    partial void OnAddTrailingSpaceChanged(bool value) => ScheduleApply();

    partial void OnShowDictationToastChanged(bool value) => ScheduleApply();

    partial void OnStartWithSystemChanged(bool value)
    {
        if (_isLoadingFromConfig)
        {
            return;
        }

        _autoStartService.SetEnabled(value);
        ScheduleApply();
    }

    partial void OnInputDeviceChanged(AudioDeviceInfo? value) => ScheduleApply();

    public void UpdateModelStatus()
    {
        var snapshot = _models.Refresh(Engine, WhisperModelSize, GigaAmModelSize);
        ModelTitle = snapshot.Title;
        IsModelDownloaded = snapshot.IsDownloaded;
        HasCurrentModel = snapshot.HasModel;
        ModelStatus = snapshot.StatusText;

        if (!IsApplying && !IsCapturingHotkey)
        {
            _status.RefreshReadiness();
        }
    }

    [RelayCommand]
    private async Task DownloadModelAsync()
    {
        await _models.DownloadAsync(
            Engine,
            WhisperModelSize,
            GigaAmModelSize,
            status => ModelStatus = status,
            applying => IsApplying = applying);
        UpdateModelStatus();
    }

    [RelayCommand]
    private void DeleteModel()
    {
        if (_models.Delete(Engine, WhisperModelSize, GigaAmModelSize, status => ModelStatus = status))
        {
            UpdateModelStatus();
        }
    }

    [RelayCommand]
    private void ToggleHotkeyCapture()
    {
        if (IsCapturingHotkey)
        {
            CancelHotkeyCapture();
            return;
        }

        _hotkeyCapture.Begin(Hotkey);
        IsCapturingHotkey = true;
        HotkeyPreview = string.Empty;
        _status.SetStatus("Loc.Status.HotkeyCapture");
    }

    public void UpdateHotkeyPreview(string preview)
    {
        _hotkeyCapture.UpdatePreview(preview);
        HotkeyPreview = preview;
        OnPropertyChanged(nameof(HotkeyDisplay));
    }

    public void ApplyCapturedHotkey(string hotkey)
    {
        var applied = _hotkeyCapture.Complete(hotkey);
        if (applied is null)
        {
            return;
        }

        Hotkey = applied;
        IsCapturingHotkey = false;
        HotkeyPreview = string.Empty;
        OnPropertyChanged(nameof(HotkeyDisplay));
        _status.RefreshReadiness();
        ScheduleApply();
    }

    public void CancelHotkeyCapture()
    {
        Hotkey = _hotkeyCapture.Cancel();
        IsCapturingHotkey = false;
        HotkeyPreview = string.Empty;
        OnPropertyChanged(nameof(HotkeyDisplay));
        _status.RefreshReadiness();
    }

    partial void OnHotkeyChanged(string value) => OnPropertyChanged(nameof(HotkeyDisplay));

    private void ScheduleApply()
    {
        if (_isLoadingFromConfig)
        {
            return;
        }

        _applyService.ScheduleApply(PrepareConfigForApplyAsync, OnApplySucceeded, OnApplyFinished);
    }

    private async Task EnsureDirectMlThenApplyAsync()
    {
        if (_directMlInstaller is null)
        {
            ScheduleApply();
            return;
        }

        IsApplying = true;
        _status.SetStatus("Loc.Status.PreparingDirectMl", busy: true);
        try
        {
            var progress = _applyService.CreateProgressReporter(
                null,
                s => _status.SetStatus(s, busy: true));
            await _directMlInstaller.EnsureInstalledAsync(progress).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(ScheduleApply);
        }
        catch (Exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SelectedComputeDevice = CpuDeviceOption;
                _status.SetStatusTemporary(
                    "Loc.Status.DirectMlFailed",
                    SettingsApplyService.StatusClearMs,
                    alert: true);
                IsApplying = false;
            });
        }
    }

    private async Task<AppConfig> PrepareConfigForApplyAsync()
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (IsCapturingHotkey)
            {
                CancelHotkeyCapture();
            }

            IsApplying = true;
            _status.SetStatus("Loc.Status.Saving", busy: true);
            return BuildConfigFromViewModel();
        });
    }

    private void OnApplySucceeded() => _home.NotifyConfigChanged();

    private void OnApplyFinished(int generation)
    {
        if (generation == _applyService.ApplyGeneration)
        {
            IsApplying = false;
        }
    }

    private AppConfig BuildConfigFromViewModel()
    {
        var config = _coordinator.Config.Clone();
        config.Hotkey = Hotkey;
        config.Engine = Engine;
        config.WhisperModelSize = WhisperModelSize;
        config.GigaAmModelSize = GigaAmModelSize;
        config.Language = Language;
        config.Device = SelectedComputeDevice?.Id ?? ExecutionProviderResolver.CpuDevice;
        config.InputDevice = InputDevice?.Id ?? string.Empty;
        config.InputMethod = SelectedInputMethod?.Id
            ?? (OperatingSystem.IsLinux() ? "auto" : "clipboard");
        config.TypeDelayMs = SelectedTypeSpeed?.DelayMs ?? 1;
        config.AddTrailingSpace = AddTrailingSpace;
        config.ShowDictationToast = ShowDictationToast;
        config.StartWithSystem = StartWithSystem;
        config.UiLanguage = SelectedUiLanguage?.Code ?? "system";
        return config;
    }

    private void LoadFromConfig()
    {
        _isLoadingFromConfig = true;
        try
        {
            var config = _coordinator.Config;
            Hotkey = config.Hotkey;
            Engine = EngineOptions.Any(e => e.Id == config.Engine)
                ? config.Engine
                : EngineOptions[0].Id;
            WhisperModelSize = config.WhisperModelSize;
            GigaAmModelSize = config.GigaAmModelSize;
            Language = config.Language;
            SelectedComputeDevice = ResolveComputeDeviceOption(config.Device);
            SelectedInputMethod = InputMethodOptions.FirstOrDefault(o => o.Id == config.InputMethod)
                ?? InputMethodOptions.First();
            SelectedTypeSpeed = TypeSpeedOptions.FirstOrDefault(o => o.DelayMs == config.TypeDelayMs)
                ?? TypeSpeedOptions[1];
            SelectedUiLanguage = UiLanguageChoices.FirstOrDefault(c =>
                string.Equals(c.Code, config.UiLanguage, StringComparison.OrdinalIgnoreCase))
                ?? UiLanguageChoices.FirstOrDefault();
            AddTrailingSpace = config.AddTrailingSpace;
            ShowDictationToast = config.ShowDictationToast;
            StartWithSystem = config.StartWithSystem;
            InputDevice = InputDevices.FirstOrDefault(d => d.Id == config.InputDevice)
                ?? InputDevices.FirstOrDefault(d => d.Name == config.InputDevice)
                ?? InputDevices.FirstOrDefault();
            UpdateModelStatus();
            OnPropertyChanged(nameof(ComputeDeviceOptions));
            EnsureValidComputeDevice();
            OnPropertyChanged(nameof(SelectedEngine));
            OnPropertyChanged(nameof(IsTypeInput));
        }
        finally
        {
            _isLoadingFromConfig = false;
        }
    }

    public void RefreshDeviceOptions()
    {
        OnPropertyChanged(nameof(ComputeDeviceOptions));
        EnsureValidComputeDevice();
    }

    private void RebuildEngineOptions()
    {
        var localized = AllEngineIds
            .Select(id => new EngineOption(id, GetEngineDisplayName(id)))
            .ToList();
        EngineOptions = localized.Where(o => _registeredEngineIds.Contains(o.Id)).ToList();
        if (EngineOptions.Count == 0)
        {
            EngineOptions = localized.Where(o => o.Id == "gigaam").ToList();
        }
    }

    private string GetEngineDisplayName(string id) => id switch
    {
        "gigaam" => _loc.Get("Loc.Engine.GigaAm"),
        "whisper" => _loc.Get("Loc.Engine.Whisper"),
        "omnilingual" => _loc.Get("Loc.Engine.Omnilingual"),
        _ => id,
    };

    private void RebuildTypeSpeedOptions()
    {
        TypeSpeedOptions =
        [
            new(_loc.Get("Loc.TypeSpeed.Fast"), 0),
            new(_loc.Get("Loc.TypeSpeed.Normal"), 1),
            new(_loc.Get("Loc.TypeSpeed.Smooth"), 5),
        ];
    }

    private void RebuildUiLanguageChoices()
    {
        UiLanguageChoices = _loc.LanguageOptions
            .Select(o => new UiLanguageChoice(o.Code, _loc.Get(o.DisplayNameKey)))
            .ToList();
    }

    private void RefreshLocalizedOptions()
    {
        var inputMethodId = SelectedInputMethod?.Id;
        var typeSpeedDelay = SelectedTypeSpeed?.DelayMs ?? 1;
        var computeDeviceId = SelectedComputeDevice?.Id;
        var uiLanguageCode = SelectedUiLanguage?.Code ?? _loc.Preference;

        RebuildEngineOptions();
        RebuildTypeSpeedOptions();
        RebuildUiLanguageChoices();

        _isLoadingFromConfig = true;
        try
        {
            SelectedInputMethod = InputMethodOptions.FirstOrDefault(o => o.Id == inputMethodId)
                ?? InputMethodOptions.FirstOrDefault();
            SelectedTypeSpeed = TypeSpeedOptions.FirstOrDefault(o => o.DelayMs == typeSpeedDelay)
                ?? TypeSpeedOptions.ElementAtOrDefault(1);
            SelectedComputeDevice = ResolveComputeDeviceOption(
                computeDeviceId ?? ExecutionProviderResolver.CpuDevice);
            SelectedUiLanguage = UiLanguageChoices.FirstOrDefault(c =>
                string.Equals(c.Code, uiLanguageCode, StringComparison.OrdinalIgnoreCase))
                ?? UiLanguageChoices.FirstOrDefault();

            OnPropertyChanged(nameof(EngineOptions));
            OnPropertyChanged(nameof(SelectedEngine));
            OnPropertyChanged(nameof(ComputeDeviceOptions));
            OnPropertyChanged(nameof(InputMethodOptions));
            OnPropertyChanged(nameof(TypeSpeedOptions));
            OnPropertyChanged(nameof(UiLanguageChoices));
            OnPropertyChanged(nameof(ModelLoadedTooltip));
            OnPropertyChanged(nameof(ModelDownloadTooltip));
            OnPropertyChanged(nameof(ModelDeleteTooltip));
            OnPropertyChanged(nameof(TypeSpeedTooltip));
            OnPropertyChanged(nameof(IsTypeInput));
            UpdateModelStatus();
        }
        finally
        {
            _isLoadingFromConfig = false;
        }
    }

    private ComputeDeviceOption ResolveComputeDeviceOption(string deviceId)
    {
        var normalized = ExecutionProviderResolver.FromConfigDevice(deviceId);
        var id = ExecutionProviderResolver.ToConfigDevice(normalized);
        return BuildComputeDeviceOptions().FirstOrDefault(o => o.Id == id) ?? CpuDeviceOption;
    }

    private void EnsureValidComputeDevice()
    {
        var options = BuildComputeDeviceOptions();
        if (SelectedComputeDevice is null || options.All(o => o.Id != SelectedComputeDevice.Id))
        {
            SelectedComputeDevice = options[0];
        }
    }
}
