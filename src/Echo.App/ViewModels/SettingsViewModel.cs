using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

public partial class SettingsViewModel : ObservableObject
{
    private readonly DictationCoordinator _coordinator;
    private readonly IAudioCapture _audio;
    private readonly HomeViewModel _home;
    private readonly AppStatusViewModel _status;
    private readonly SettingsApplyService _applyService;
    private readonly IDirectMlAvailability _directMlAvailability;
    private readonly IAutoStartService _autoStartService;
    private readonly HotkeyCaptureController _hotkeyCapture;
    private readonly ModelSettingsController _models;
    private readonly DirectMlRuntimeInstaller? _directMlInstaller;
    private readonly IReadOnlyList<EngineOption> _allEngineOptions;
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
        HasCurrentModel ? $"{ModelTitle} скачана и готова к распознаванию" : string.Empty;

    public string ModelDownloadTooltip =>
        HasCurrentModel ? $"Скачать {ModelTitle} на устройство" : string.Empty;

    public string ModelDeleteTooltip =>
        HasCurrentModel ? $"Удалить файлы {ModelTitle} с устройства" : string.Empty;

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
        _directMlInstaller = directMlInstaller;

        var registered = engines.Select(e => e.EngineId).ToHashSet(StringComparer.Ordinal);
        _allEngineOptions =
        [
            new("gigaam", "GigaAM (русский)"),
            new("whisper", "Whisper (мультиязычный)"),
            new("omnilingual", "Omnilingual (1600 языков)"),
        ];
        EngineOptions = _allEngineOptions.Where(o => registered.Contains(o.Id)).ToList();
        if (EngineOptions.Count == 0)
        {
            EngineOptions = _allEngineOptions.Where(o => o.Id == "gigaam").ToList();
        }

        LoadFromConfig();
    }

    public bool IsAutoStartSupported => _autoStartService.IsSupported;

    private static readonly ComputeDeviceOption CpuDeviceOption = new(
        ExecutionProviderResolver.CpuDevice,
        "Процессор",
        "Универсальный режим. Работает на любом ПК.");

    private static readonly ComputeDeviceOption DirectMlDeviceOption = new(
        ExecutionProviderResolver.DirectMlDevice,
        "GPU (DirectML)",
        "Ускорение через DirectML на Windows (AMD Radeon, Intel, NVIDIA). При первом выборе скачается ~30 МБ.");

    public IReadOnlyList<EngineOption> EngineOptions { get; private set; }
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
    public IReadOnlyList<InputMethodOption> InputMethodOptions =>
        OperatingSystem.IsLinux()
            ? BuildLinuxInputMethodOptions()
            :
            [
                new("clipboard", "Вставка из буфера", "Быстрая вставка. Содержимое буфера обмена восстанавливается после вставки."),
                new("type", "Печать", "Посимвольный ввод через эмуляцию клавиатуры."),
            ];

    private static IReadOnlyList<InputMethodOption> BuildLinuxInputMethodOptions()
    {
        if (LinuxDependencyCatalog.UsesGnomeWaylandYdotool)
        {
            return
            [
                new(
                    "auto",
                    "Авто",
                    "На GNOME Wayland — вставка через буфер Echo и ydotool (Ctrl+V), без wl-copy. "
                    + "Если мигает панель — перезапустите Echo."),
                new(
                    "clipboard",
                    "Вставка из буфера",
                    "Мгновенная вставка всего текста через буфер и Ctrl+V (ydotool)."),
            ];
        }

        return
        [
            new(
                "auto",
                "Авто",
                "Echo выберет лучший способ: AT-SPI, ydotool, xdotool или wtype. "
                + "Если автовставка недоступна — текст копируется в буфер."),
            new(
                "clipboard",
                "Вставка из буфера",
                "Сначала AT-SPI; иначе эмуляция Ctrl+V через ydotool, xdotool или wtype."),
        ];
    }

    public IReadOnlyList<TypeSpeedOption> TypeSpeedOptions { get; } =
    [
        new("Быстро", 0),
        new("Нормально", 1),
        new("Плавно", 5),
    ];
    public IReadOnlyList<string> Languages { get; } = ["auto", "ru", "en"];

    public bool IsTypeInput =>
        !OperatingSystem.IsLinux() && SelectedInputMethod?.Id == "type";

    public string TypeSpeedTooltip => "Задержка между символами при методе «Печать»";

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
        _status.SetStatus(HotkeyCaptureController.CaptureStatus);
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
        _status.SetStatus("Подготовка DirectML…", busy: true);
        try
        {
            var progress = _applyService.CreateProgressReporter(null, s => _status.SetStatus(s, busy: true));
            await _directMlInstaller.EnsureInstalledAsync(progress).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(ScheduleApply);
        }
        catch (Exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SelectedComputeDevice = CpuDeviceOption;
                _status.SetStatusTemporary(
                    "Не удалось скачать DirectML — оставлен CPU",
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
