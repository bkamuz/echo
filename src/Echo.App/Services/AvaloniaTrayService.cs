using Avalonia.Controls;
using Avalonia.Platform;
using echo.Abstractions.Platform;

namespace echo.App.Services;

public sealed class AvaloniaTrayService : ITrayStateService
{
    private readonly TrayIcon _tray;
    private readonly WindowIcon _idleIcon;
    private readonly WindowIcon _recordingIcon;
    private readonly WindowIcon _processingIcon;

    public AvaloniaTrayService()
    {
        _idleIcon = LoadTrayIcon("sleep");
        _recordingIcon = LoadTrayIcon("listen");
        _processingIcon = LoadTrayIcon("processing");

        _tray = new TrayIcon
        {
            Icon = _idleIcon,
            ToolTipText = "Echo — готов",
            IsVisible = true,
        };
    }

    public void SetState(DictationOverlayState state)
    {
        switch (state)
        {
            case DictationOverlayState.Hidden:
                _tray.Icon = _idleIcon;
                _tray.ToolTipText = "Echo — готов";
                break;
            case DictationOverlayState.Recording:
                _tray.Icon = _recordingIcon;
                _tray.ToolTipText = "Echo — слушаю";
                break;
            case DictationOverlayState.Processing:
                _tray.Icon = _processingIcon;
                _tray.ToolTipText = "Echo — обработка...";
                break;
        }
    }

    private static WindowIcon LoadTrayIcon(string name)
    {
        var uri = new Uri($"avares://echo.App/Resources/{name}.png");
        using var stream = AssetLoader.Open(uri);
        return new WindowIcon(stream);
    }
}
