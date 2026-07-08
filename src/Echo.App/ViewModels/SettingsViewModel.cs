using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using echo.Core;
using echo.Abstractions.Platform;

namespace echo.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly DictationCoordinator _coordinator;
    private readonly IAudioCapture _audio;
    private readonly IHotkeyService _hotkeyService;
    private readonly HomeViewModel _home;
    private string _savedHotkey = string.Empty;

    [ObservableProperty] private string _hotkey = string.Empty;
    [ObservableProperty] private string _engine = string.Empty;
    [ObservableProperty] private string _whisperModelSize = string.Empty;
    [ObservableProperty] private string _device = string.Empty;
    [ObservableProperty] private AudioDeviceInfo? _inputDevice;
    [ObservableProperty] private string _inputMethod = string.Empty;
    [ObservableProperty] private bool _addTrailingSpace;
    [ObservableProperty] private bool _isCapturingHotkey;
    [ObservableProperty] private string _hotkeyCaptureHint = string.Empty;
    [ObservableProperty] private string _hotkeyPreview = string.Empty;

    public SettingsViewModel(
        DictationCoordinator coordinator,
        IAudioCapture audio,
        IHotkeyService hotkey,
        HomeViewModel home)
    {
        _coordinator = coordinator;
        _audio = audio;
        _hotkeyService = hotkey;
        _home = home;
        LoadFromConfig();
    }

    public IReadOnlyList<string> Engines => AppConfig.Engines;
    public IReadOnlyList<string> Devices => AppConfig.Devices;
    public IReadOnlyList<string> WhisperSizes => AppConfig.WhisperSizes;
    public IReadOnlyList<string> InputMethods { get; } = ["type", "clipboard"];

    public string HotkeyDisplay =>
        IsCapturingHotkey && !string.IsNullOrEmpty(HotkeyPreview)
            ? HotkeyPreview
            : HotkeyTokens.ToDisplay(Hotkey);

    public string HotkeyCaptureButtonText => IsCapturingHotkey ? "Отмена" : "Назначить";

    public IReadOnlyList<AudioDeviceInfo> InputDevices => _audio.ListInputDevices();

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
        HotkeyCaptureHint = "Удерживайте комбинацию и отпустите все клавиши…";
        OnPropertyChanged(nameof(HotkeyCaptureButtonText));
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
        HotkeyCaptureHint = string.Empty;
        OnPropertyChanged(nameof(HotkeyDisplay));
        OnPropertyChanged(nameof(HotkeyCaptureButtonText));
        _hotkeyService.Configure(Hotkey);
        _hotkeyService.Start();
    }

    public void CancelHotkeyCapture()
    {
        Hotkey = _savedHotkey;
        IsCapturingHotkey = false;
        HotkeyPreview = string.Empty;
        HotkeyCaptureHint = string.Empty;
        OnPropertyChanged(nameof(HotkeyDisplay));
        OnPropertyChanged(nameof(HotkeyCaptureButtonText));
        _hotkeyService.Configure(_coordinator.Config.Hotkey);
        _hotkeyService.Start();
    }

    partial void OnHotkeyChanged(string value)
    {
        OnPropertyChanged(nameof(HotkeyDisplay));
    }

    [RelayCommand]
    private void Save()
    {
        if (IsCapturingHotkey)
        {
            CancelHotkeyCapture();
        }

        var config = _coordinator.Config;
        config.Hotkey = Hotkey;
        config.Engine = Engine;
        config.WhisperModelSize = WhisperModelSize;
        config.Device = Device;
        config.InputDevice = InputDevice?.Name ?? string.Empty;
        config.InputMethod = InputMethod;
        config.AddTrailingSpace = AddTrailingSpace;
        _coordinator.SaveConfig(config);
        _home.NotifyConfigChanged();
    }

    private void LoadFromConfig()
    {
        var config = _coordinator.Config;
        Hotkey = config.Hotkey;
        _savedHotkey = Hotkey;
        Engine = config.Engine;
        WhisperModelSize = config.WhisperModelSize;
        Device = config.Device;
        InputMethod = config.InputMethod;
        AddTrailingSpace = config.AddTrailingSpace;
        InputDevice = InputDevices.FirstOrDefault(d => d.Name == config.InputDevice)
            ?? InputDevices.FirstOrDefault();
    }
}
