using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using echo.Abstractions.Platform;
using System.Runtime.InteropServices;

namespace echo.App.Services;

public sealed class AvaloniaTrayService : ITrayStateService
{
    private readonly TrayIcon _tray;
    private readonly WindowIcon _idleIcon;
    private readonly WindowIcon _recordingIcon;
    private readonly WindowIcon _processingIcon;

    public AvaloniaTrayService()
    {
        _idleIcon = CreateIcon(Colors.Gray);
        _recordingIcon = CreateIcon(Colors.Red);
        _processingIcon = CreateIcon(Colors.Orange);

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

    private static WindowIcon CreateIcon(Color color)
    {
        const int size = 16;
        var writeable = new WriteableBitmap(
            new PixelSize(size, size),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);

        var pixels = new byte[size * size * 4];
        var center = size / 2;
        var radiusSq = (size / 2 - 1) * (size / 2 - 1);

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var idx = (y * size + x) * 4;
                var dx = x - center;
                var dy = y - center;
                if (dx * dx + dy * dy <= radiusSq)
                {
                    pixels[idx + 0] = color.B;
                    pixels[idx + 1] = color.G;
                    pixels[idx + 2] = color.R;
                    pixels[idx + 3] = 255;
                }
            }
        }

        using var buffer = writeable.Lock();
        Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);

        return new WindowIcon(writeable);
    }
}
