using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using echo.Abstractions.Platform;

namespace echo.App.Services;

public sealed class AvaloniaTrayService : ITrayStateService
{
    private readonly TrayIcon _tray;
    private readonly WindowIcon _idleIcon;
    private readonly WindowIcon _recordingIcon;
    private readonly WindowIcon _processingIcon;
    private Window? _mainWindow;

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

    public void AttachMainWindow(Window window)
    {
        _mainWindow = window;
        window.Icon = _idleIcon;
    }

    public void SetState(DictationOverlayState state)
    {
        var (icon, tooltip) = state switch
        {
            DictationOverlayState.Hidden => (_idleIcon, "Echo — готов"),
            DictationOverlayState.Recording => (_recordingIcon, "Echo — слушаю"),
            DictationOverlayState.Processing => (_processingIcon, "Echo — обработка..."),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };

        _tray.Icon = icon;
        _tray.ToolTipText = tooltip;

        if (_mainWindow is null)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            _mainWindow.Icon = icon;
        }
        else
        {
            Dispatcher.UIThread.Post(() => _mainWindow.Icon = icon);
        }
    }

    private static WindowIcon LoadTrayIcon(string name)
    {
        var uri = new Uri($"avares://echo.App/Resources/{name}.png");
        using var stream = AssetLoader.Open(uri);
        return new WindowIcon(stream);
    }
}
