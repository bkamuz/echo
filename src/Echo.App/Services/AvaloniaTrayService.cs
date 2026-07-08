using Avalonia.Controls;
using Avalonia.Platform;
using echo.Abstractions.Platform;
using SkiaSharp;
using Svg.Skia;

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
        var uri = new Uri($"avares://echo.App/Resources/{name}.svg");
        using var stream = AssetLoader.Open(uri);
        using var svg = new SKSvg();
        if (svg.Load(stream) is null || svg.Picture is null)
        {
            throw new InvalidOperationException($"Failed to load tray icon: {name}.svg");
        }

        using var png = new MemoryStream();
        const float scale = 32f / 44f;
        using var bitmap = svg.Picture.ToBitmap(
            SKColors.Empty,
            scale,
            scale,
            SKColorType.Rgba8888,
            SKAlphaType.Premul,
            SKColorSpace.CreateSrgb());
        if (bitmap is null)
        {
            throw new InvalidOperationException($"Failed to rasterize tray icon: {name}.svg");
        }

        using (bitmap)
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            png.Write(data.ToArray());
        }

        png.Position = 0;
        return new WindowIcon(png);
    }
}
