using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using echo.Core;
using echo.Abstractions.Core;
using echo.Abstractions.Platform;

namespace echo.App.ViewModels;

public sealed record TypeSpeedOption(string Label, int DelayMs)
{
    public override string ToString() => Label;
}

public partial class SettingsViewModel : ObservableObject
{
    private const int ApplyDebounceMs = 300;
    private const int StatusClearMs = 2500;
    private const string HotkeyCaptureStatus = "Удерживайте комбинацию и отпустите все клавиши…";

    private readonly DictationCoordinator _coordinator;
    private readonly ModelDownloader _downloader;
    private readonly IAudioCapture _audio;
    private readonly IHotkeyService _hotkeyService;
    private readonly HomeViewModel _home;
    private readonly AppStatusViewModel _status;
    private string _savedHotkey = string.Empty;
    private bool _isLoadingFromConfig;
    private int _applyGeneration;
    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _applyCts;

    [ObservableProperty] private string _hotkey = string.Empty;
    [ObservableProperty] private string _engine = string.Empty;
    [ObservableProperty] private string _whisperModelSize = string.Empty;
    [ObservableProperty] private string _gigaAmModelSize = string.Empty;
    [ObservableProperty] private string _language = string.Empty;
    [ObservableProperty] private string _device = string.Empty;
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
    [NotifyPropertyChangedFor(nameof(ShowModelDownloadButton))]
    private bool _hasCurrentModel;
    [ObservableProperty] private string _modelLoadedTooltip = string.Empty;
    [ObservableProperty] private string _modelDownloadTooltip = string.Empty;
    [ObservableProperty] private string _modelDeleteTooltip = string.Empty;
    [ObservableProperty] private bool _isApplying;

    public bool ShowModelDownloadButton => HasCurrentModel && !IsModelDownloaded;

    public SettingsViewModel(
        DictationCoordinator coordinator,
        ModelDownloader downloader,
        IAudioCapture audio,
        IHotkeyService hotkey,
        HomeViewModel home,
        AppStatusViewModel status)
    {
        _coordinator = coordinator;
        _downloader = downloader;
        _audio = audio;
        _hotkeyService = hotkey;
        _home = home;
        _status = status;
        LoadFromConfig();
    }

    public IReadOnlyList<string> Engines => AppConfig.Engines;
    public IReadOnlyList<EngineOption> EngineOptions { get; } =
    [
        new("gigaam", "GigaAM (русский)"),
        new("whisper", "Whisper (мультиязычный)"),
        new("omnilingual", "Omnilingual (1600 языков)"),
    ];
    public IReadOnlyList<string> Devices => AppConfig.Devices;
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

    partial void OnDeviceChanged(string value) => ScheduleApply();

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
            ModelStatus = "Неизвестная модель";
            IsModelDownloaded = false;
            HasCurrentModel = false;
            ModelLoadedTooltip = string.Empty;
            ModelDownloadTooltip = string.Empty;
            ModelDeleteTooltip = string.Empty;
            return;
        }

        var downloaded = spec.IsDownloaded();
        IsModelDownloaded = downloaded;
        HasCurrentModel = true;
        ModelLoadedTooltip = $"{spec.Title} скачана и готова к распознаванию";
        ModelDownloadTooltip = $"Скачать {spec.Title} на устройство";
        ModelDeleteTooltip = $"Удалить файлы {spec.Title} с устройства";
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
            var progress = CreateProgressReporter(null, ApplyDownloadProgress);
            await _downloader.DownloadAsync(spec, progress).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ModelStatus = $"{spec.Title} ✓ загружена";
                UpdateModelStatus();
            });

            var warmupProgress = CreateApplyProgress(_applyGeneration);
            await _coordinator.TryWarmupCurrentModelAsync(warmupProgress).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _home.NotifyConfigChanged();
                _status.SetStatusTemporary($"{spec.Title} ✓ загружена", StatusClearMs);
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
        _status.SetStatusTemporary($"{spec.Title} удалена", StatusClearMs);
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

        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        var debounceToken = _debounceCts.Token;
        _ = DebouncedApplyAsync(debounceToken);
    }

    private async Task DebouncedApplyAsync(CancellationToken debounceToken)
    {
        try
        {
            await Task.Delay(ApplyDebounceMs, debounceToken).ConfigureAwait(false);
            await ApplyChangesAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ApplyChangesAsync()
    {
        var generation = ++_applyGeneration;
        _applyCts?.Cancel();
        _applyCts?.Dispose();
        _applyCts = new CancellationTokenSource();
        var ct = _applyCts.Token;

        var config = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (IsCapturingHotkey)
            {
                CancelHotkeyCapture();
            }

            IsApplying = true;
            _status.SetStatus("Сохранение…", busy: true);
            return BuildConfigFromViewModel();
        });

        try
        {
            var progress = CreateApplyProgress(generation);
            await _coordinator.SaveConfigAsync(config, progress, ct).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation != _applyGeneration)
                {
                    return;
                }

                _home.NotifyConfigChanged();
                _status.SetStatusTemporary("Готово", StatusClearMs);
            });
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation != _applyGeneration)
                {
                    return;
                }

                _status.SetStatusTemporary("Модель не загружена — скачайте в настройках", StatusClearMs);
            });
        }
        catch (Exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation != _applyGeneration)
                {
                    return;
                }

                _status.SetStatusTemporary("Ошибка применения настроек", StatusClearMs);
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation == _applyGeneration)
                {
                    IsApplying = false;
                }
            });
        }
    }

    private IProgress<string> CreateApplyProgress(int generation) =>
        CreateProgressReporter(generation, ApplyProgressStatus);

    private IProgress<string> CreateProgressReporter(int? generation, Action<string> apply) =>
        new Progress<string>(status =>
        {
            if (generation.HasValue && generation.Value != _applyGeneration)
            {
                return;
            }

            void Run() => apply(status);
            if (Dispatcher.UIThread.CheckAccess())
            {
                Run();
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (generation.HasValue && generation.Value != _applyGeneration)
                    {
                        return;
                    }

                    Run();
                });
            }
        });

    private void ApplyProgressStatus(string status)
    {
        var normalized = status.Trim();
        if (normalized.StartsWith("Готово", StringComparison.Ordinal))
        {
            return;
        }

        _status.SetStatus(normalized, busy: true);
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
        config.Device = Device;
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
            Device = config.Device;
            SelectedInputMethod = InputMethodOptions.FirstOrDefault(o => o.Id == config.InputMethod)
                ?? InputMethodOptions[0];
            SelectedTypeSpeed = TypeSpeedOptions.FirstOrDefault(o => o.DelayMs == config.TypeDelayMs)
                ?? TypeSpeedOptions[1];
            AddTrailingSpace = config.AddTrailingSpace;
            InputDevice = InputDevices.FirstOrDefault(d => d.Name == config.InputDevice)
                ?? InputDevices.FirstOrDefault();
            UpdateModelStatus();
            OnPropertyChanged(nameof(SelectedEngine));
            OnPropertyChanged(nameof(IsTypeInput));
        }
        finally
        {
            _isLoadingFromConfig = false;
        }
    }
}
