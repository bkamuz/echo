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
    private readonly ITaskbarIconSync? _taskbarIconSync;
    private readonly byte[] _idleIconBytes;
    private readonly byte[] _recordingIconBytes;
    private readonly byte[] _processingIconBytes;
    private Window? _mainWindow;

    public AvaloniaTrayService(ITaskbarIconSync? taskbarIconSync = null)
    {
        _taskbarIconSync = taskbarIconSync;
        _idleIconBytes = LoadTrayIconBytes("sleep");
        _recordingIconBytes = LoadTrayIconBytes("listen");
        _processingIconBytes = LoadTrayIconBytes("processing");
        _idleIcon = LoadTrayIcon(_idleIconBytes);
        _recordingIcon = LoadTrayIcon(_recordingIconBytes);
        _processingIcon = LoadTrayIcon(_processingIconBytes);

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

        void TryAttachHandle()
        {
            var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle != IntPtr.Zero)
            {
                _taskbarIconSync?.Attach(handle);
            }
        }

        window.Opened += (_, _) => TryAttachHandle();
        if (window.IsVisible)
        {
            TryAttachHandle();
        }
    }

    public void SetState(DictationOverlayState state)
    {
        var (icon, iconBytes, tooltip) = state switch
        {
            DictationOverlayState.Hidden => (_idleIcon, _idleIconBytes, "Echo — готов"),
            DictationOverlayState.Recording => (_recordingIcon, _recordingIconBytes, "Echo — слушаю"),
            DictationOverlayState.Processing => (_processingIcon, _processingIconBytes, "Echo — обработка..."),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };

        _tray.Icon = icon;
        _tray.ToolTipText = tooltip;

        if (_mainWindow is null)
        {
            return;
        }

        void Apply()
        {
            _mainWindow.Icon = icon;

            var handle = _mainWindow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle != IntPtr.Zero)
            {
                _taskbarIconSync?.Attach(handle);
            }

            _taskbarIconSync?.ApplyIcon(iconBytes, state, tooltip);
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    private static byte[] LoadTrayIconBytes(string name)
    {
        var uri = new Uri($"avares://echo.App/Resources/{name}.png");
        using var stream = AssetLoader.Open(uri);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static WindowIcon LoadTrayIcon(byte[] bytes)
    {
        return new WindowIcon(new MemoryStream(bytes));
    }
}
