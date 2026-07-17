using echo.Abstractions.Platform;
using echo.Core;

namespace echo.App.Services;

public sealed class HotkeyCaptureController
{
    private readonly IHotkeyService _hotkeyService;
    private readonly DictationCoordinator _coordinator;
    private string _savedHotkey = string.Empty;

    public HotkeyCaptureController(
        IHotkeyService hotkeyService,
        DictationCoordinator coordinator)
    {
        _hotkeyService = hotkeyService;
        _coordinator = coordinator;
    }

    public bool IsCapturing { get; private set; }
    public string Preview { get; private set; } = string.Empty;

    public void Begin(string currentHotkey)
    {
        _savedHotkey = currentHotkey;
        IsCapturing = true;
        Preview = string.Empty;
        _hotkeyService.Stop();
    }

    public void UpdatePreview(string preview) => Preview = preview;

    public string? Complete(string hotkey)
    {
        if (!HotkeyTokens.IsValid(hotkey))
        {
            return null;
        }

        IsCapturing = false;
        Preview = string.Empty;
        _hotkeyService.Configure(hotkey);
        _hotkeyService.Start();
        return hotkey;
    }

    public string Cancel()
    {
        IsCapturing = false;
        Preview = string.Empty;
        var restored = string.IsNullOrEmpty(_savedHotkey)
            ? _coordinator.Config.Hotkey
            : _savedHotkey;
        _hotkeyService.Configure(restored);
        _hotkeyService.Start();
        return restored;
    }
}
