using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using echo.App.Services;
using echo.Core;
using echo.Abstractions.Core;
using echo.Abstractions.Engines;
using echo.Abstractions.Platform;

namespace echo.App.ViewModels;

public sealed record TypeSpeedOption(string Label, int DelayMs)
{
    public override string ToString() => Label;
}

public partial class SettingsViewModel : ObservableObject
{
    private const string HotkeyCaptureStatus = "Удерживайте комбинацию и отпустите все клавиши…";

    private readonly DictationCoordinator _coordinator;
    private readonly ModelDownloader _downloader;
    private readonly IAudioCapture _audio;
    private readonly IHotkeyService _hotkeyService;
    private readonly HomeViewModel _home;
    private readonly AppStatusViewModel _status;
    private readonly SettingsApplyService _applyService;
    private readonly IDirectMlAvailability _directMlAvailability;
    private string _savedHotkey = string.Empty;
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
    [ObservableProperty] private bool _addTrailingSpace;
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
        HasCurrentModel ? $"{ModelTitle} скачана и готова к распознаванию" : string.Empty;

    public string ModelDownloadTooltip =>
        HasCurrentModel ? $"Скачать {ModelTitle} на устройство" : string.Empty;

    public string ModelDeleteTooltip =>
        HasCurrentModel ? $"Удалить файлы {ModelTitle} с устройства" : string.Empty;

    public SettingsViewModel(
        DictationCoordinator coordinator,
        ModelDownloader downloader,
        IAudioCapture audio,
        IHotkeyService hotkey,
        HomeViewModel home,
        AppStatusViewModel status,
        SettingsApplyService applyService,
        IDirectMlAvailability directMlAvailability)
    {
        _coordinator = coordinator;
        _downloader = downloader;
        _audio = audio;
        _hotkeyService = hotkey;
        _home = home;
        _status = status;
        _applyService = applyService;
        _directMlAvailability = directMlAvailability;
        LoadFromConfig();
    }

    private static readonly ComputeDeviceOption CpuDeviceOption = new(
        ExecutionProviderResolver.CpuDevice,
        "Процессор",
        "Универсальный режим. Работает на любом ПК.");

    private static readonly ComputeDeviceOption DirectMlDeviceOption = new(
        ExecutionProviderResolver.DirectMlDevice,
        "GPU (DirectML)",
        "Ускорение через DirectML на Windows (AMD Radeon, Intel, NVIDIA).");

    public IReadOnlyList<EngineOption> EngineOptions { get; } =
    [
        new("gigaam", "GigaAM (русский)"),
        new("whisper", "Whisper (мультиязычный)"),
        new("omnilingual", "Omnilingual (1600 языков)"),
    ];
    public IReadOnlyList<ComputeDeviceOption> ComputeDeviceOptions => BuildComputeDeviceOptions();

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
    public IReadOnlyList<InputMethodOption> InputMethodOptions { get; } =
    [
        new("clipboard", "Вставка из буфера", "Быстрая вставка. Содержимое буфера обмена восстанавливается после вставки."),
        new("type", "Печать", "Посимвольный ввод через эмуляцию клавиатуры."),
    ];
    public IReadOnlyList<TypeSpeedOption> TypeSpeedOptions { get; } =
    [
        new("Быстро", 0),
        new("Нормально", 1),
        new("Плавно", 5),
    ];
    public IReadOnlyList<string> Languages { get; } = ["auto", "ru", "en"];

    public bool IsTypeInput => SelectedInputMethod?.Id == "type";

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

    partial void OnSelectedComputeDeviceChanged(ComputeDeviceOption? value) => ScheduleApply();

    partial void OnSelectedInputMethodChanged(InputMethodOption? value)
    {
        OnPropertyChanged(nameof(IsTypeInput));
        ScheduleApply();
    }

    partial void OnSelectedTypeSpeedChanged(TypeSpeedOption? value) => ScheduleApply();

    partial void OnAddTrailingSpaceChanged(bool value) => ScheduleApply();

    partial void OnInputDeviceChanged(AudioDeviceInfo? value) => ScheduleApply();

    public void UpdateModelStatus()
    {
        var spec = CurrentModelSpec();
        if (spec is null)
        {
            ModelTitle = string.Empty;
            IsModelDownloaded = false;
            HasCurrentModel = false;
            ModelStatus = "Неизвестная модель";
            return;
        }

        var downloaded = spec.IsDownloaded();
        ModelTitle = spec.Title;
        IsModelDownloaded = downloaded;
        HasCurrentModel = true;
        ModelStatus = downloaded
            ? $"{spec.Title} ✓ загружена"
            : $"{spec.Title} — не загружена";
    }

    private ModelSpec? CurrentModelSpec() =>
        ModelRegistry.SpecForEngine(Engine, WhisperModelSize, GigaAmModelSize);

    [RelayCommand]
    private async Task DownloadModelAsync()
    {
        var spec = CurrentModelSpec();
        if (spec is null || spec.IsDownloaded())
        {
            return;
        }

        IsApplying = true;
        var downloadLabel = $"Скачивание {spec.Title}…";
        ModelStatus = downloadLabel;
        _status.SetStatus(downloadLabel, busy: true);
        try
        {
            var progress = _applyService.CreateProgressReporter(null, ApplyDownloadProgress);
            await _downloader.DownloadAsync(spec, progress).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ModelStatus = $"{spec.Title} ✓ загружена";
                UpdateModelStatus();
            });

            var warmupProgress = _applyService.CreateStatusProgress(_applyService.ApplyGeneration);
            await _coordinator.TryWarmupCurrentModelAsync(warmupProgress).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _home.NotifyConfigChanged();
                _status.SetStatusTemporary($"{spec.Title} ✓ загружена", SettingsApplyService.StatusClearMs);
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsApplying = false);
        }
    }

    [RelayCommand]
    private void DeleteModel()
    {
        var spec = CurrentModelSpec();
        if (spec is null || !spec.IsDownloaded())
        {
            return;
        }

        _downloader.Delete(spec);
        ModelStatus = $"{spec.Title} удалена";
        UpdateModelStatus();
        _status.SetStatusTemporary($"{spec.Title} удалена", SettingsApplyService.StatusClearMs);
    }

    [RelayCommand]
    private void ToggleHotkeyCapture()
    {
        if (IsCapturingHotkey)
        {
            CancelHotkeyCapture();
            return;
        }

        _savedHotkey = Hotkey;
        IsCapturingHotkey = true;
        HotkeyPreview = string.Empty;
        _status.SetStatus(HotkeyCaptureStatus);
        _hotkeyService.Stop();
    }

    public void UpdateHotkeyPreview(string preview)
    {
        HotkeyPreview = preview;
        OnPropertyChanged(nameof(HotkeyDisplay));
    }

    public void ApplyCapturedHotkey(string hotkey)
    {
        Hotkey = hotkey;
        IsCapturingHotkey = false;
        HotkeyPreview = string.Empty;
        OnPropertyChanged(nameof(HotkeyDisplay));
        _status.SetStatus(AppStatusViewModel.ReadyStatus);
        _hotkeyService.Configure(Hotkey);
        _hotkeyService.Start();
        ScheduleApply();
    }

    public void CancelHotkeyCapture()
    {
        Hotkey = _savedHotkey;
        IsCapturingHotkey = false;
        HotkeyPreview = string.Empty;
        OnPropertyChanged(nameof(HotkeyDisplay));
        _status.SetStatus(AppStatusViewModel.ReadyStatus);
        _hotkeyService.Configure(_coordinator.Config.Hotkey);
        _hotkeyService.Start();
    }

    partial void OnHotkeyChanged(string value)
    {
        OnPropertyChanged(nameof(HotkeyDisplay));
    }

    private void ScheduleApply()
    {
        if (_isLoadingFromConfig)
        {
            return;
        }

        _applyService.ScheduleApply(PrepareConfigForApplyAsync, OnApplySucceeded, OnApplyFinished);
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
            _status.SetStatus("Сохранение…", busy: true);
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

    private void ApplyDownloadProgress(string status)
    {
        var normalized = status.Trim();
        var isTerminal = normalized.StartsWith("Готово", StringComparison.Ordinal);
        ModelStatus = normalized;
        _status.SetStatus(normalized, busy: !isTerminal);
    }

    private AppConfig BuildConfigFromViewModel()
    {
        var config = _coordinator.Config;
        config.Hotkey = Hotkey;
        config.Engine = Engine;
        config.WhisperModelSize = WhisperModelSize;
        config.GigaAmModelSize = GigaAmModelSize;
        config.Language = Language;
        config.Device = SelectedComputeDevice?.Id ?? ExecutionProviderResolver.CpuDevice;
        config.InputDevice = InputDevice?.Name ?? string.Empty;
        config.InputMethod = SelectedInputMethod?.Id ?? "clipboard";
        config.TypeDelayMs = SelectedTypeSpeed?.DelayMs ?? 1;
        config.AddTrailingSpace = AddTrailingSpace;
        return config;
    }

    private void LoadFromConfig()
    {
        _isLoadingFromConfig = true;
        try
        {
            var config = _coordinator.Config;
            Hotkey = config.Hotkey;
            _savedHotkey = Hotkey;
            Engine = config.Engine;
            WhisperModelSize = config.WhisperModelSize;
            GigaAmModelSize = config.GigaAmModelSize;
            Language = config.Language;
            SelectedComputeDevice = ResolveComputeDeviceOption(config.Device);
            SelectedInputMethod = InputMethodOptions.FirstOrDefault(o => o.Id == config.InputMethod)
                ?? InputMethodOptions[0];
            SelectedTypeSpeed = TypeSpeedOptions.FirstOrDefault(o => o.DelayMs == config.TypeDelayMs)
                ?? TypeSpeedOptions[1];
            AddTrailingSpace = config.AddTrailingSpace;
            InputDevice = InputDevices.FirstOrDefault(d => d.Name == config.InputDevice)
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
