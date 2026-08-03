using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using echo.Abstractions.Platform;
using echo.Platform.Windows;

namespace echo.App.Views;

public partial class DictationCursorOverlayWindow : Window
{
    private const double OffsetXDip = 12;
    private const double OffsetYDip = -28;
    private const double IconSizeDip = 24;
    private const double MeterWidthDip = 72;
    private const double GapDip = 4;
    private const double IconOnlyWidthDip = IconSizeDip;
    private const double WithMeterWidthDip = IconSizeDip + GapDip + MeterWidthDip;

    // Accent #39FF14
    private const byte AccentR = 0x39;
    private const byte AccentG = 0xFF;
    private const byte AccentB = 0x14;

    private const int SpecWidth = 68;
    private const int SpecHeight = 20;

    private WriteableBitmap? _spectrogram;
    private byte[]? _pixels;
    private int _stride;
    private double _layoutWidthDip = IconOnlyWidthDip;
    private int _anchorX;
    private int _anchorY;
    private bool _hasAnchor;

    public DictationCursorOverlayWindow()
    {
        InitializeComponent();
        if (OperatingSystem.IsWindows())
        {
            Opened += (_, _) =>
            {
                if (OperatingSystem.IsWindows())
                {
                    WindowsNoActivateWindow.TryApply(TryGetPlatformHandle()?.Handle ?? 0);
                }
            };
        }
    }

    public void Present(Bitmap icon, int cursorX, int cursorY, bool showMeter)
    {
        IconImage.Source = icon;
        SetMeterVisible(showMeter);

        if (!IsVisible)
        {
            Show();
        }

        _anchorX = cursorX;
        _anchorY = cursorY;
        _hasAnchor = true;
        PositionNearCursor(cursorX, cursorY);
    }

    public void UpdateIcon(Bitmap icon) => IconImage.Source = icon;

    public void SetMeterVisible(bool visible)
    {
        MeterPanel.IsVisible = visible;
        _layoutWidthDip = visible ? WithMeterWidthDip : IconOnlyWidthDip;
        Width = _layoutWidthDip;

        if (!visible)
        {
            ClearSpectrogram();
        }
        else
        {
            EnsureSpectrogram();
        }

        if (_hasAnchor && IsVisible)
        {
            PositionNearCursor(_anchorX, _anchorY);
        }
    }

    public void UpdateSpectrum(ReadOnlySpan<float> bands)
    {
        if (!MeterPanel.IsVisible || bands.Length == 0)
        {
            return;
        }

        EnsureSpectrogram();
        if (_spectrogram is null || _pixels is null)
        {
            return;
        }

        // Scroll left by 1 column.
        for (var y = 0; y < SpecHeight; y++)
        {
            var row = y * _stride;
            Buffer.BlockCopy(_pixels, row + 4, _pixels, row, _stride - 4);
        }

        // Newest column on the right: low freq bottom, log spectrum stretched up.
        var x = SpecWidth - 1;
        for (var y = 0; y < SpecHeight; y++)
        {
            var t = (SpecHeight - 1 - y) / (float)(SpecHeight - 1);
            var bandPos = t * (bands.Length - 1);
            var i0 = (int)bandPos;
            var i1 = Math.Min(i0 + 1, bands.Length - 1);
            var frac = bandPos - i0;
            var level = Math.Clamp(bands[i0] * (1f - frac) + bands[i1] * frac, 0f, 1f);
            // Mild gamma so quiet speech still reads on the dark meter strip.
            var paint = level <= 0f ? 0f : MathF.Pow(level, 0.65f);

            var offset = y * _stride + x * 4;
            _pixels[offset] = (byte)(AccentB * paint);
            _pixels[offset + 1] = (byte)(AccentG * paint);
            _pixels[offset + 2] = (byte)(AccentR * paint);
            _pixels[offset + 3] = (byte)(paint * 255f);
        }

        using var fb = _spectrogram.Lock();
        FlushPixels(fb.Address);
        SpectrogramImage.InvalidateVisual();
    }

    public void Dismiss()
    {
        ClearSpectrogram();
        MeterPanel.IsVisible = false;
        _layoutWidthDip = IconOnlyWidthDip;
        Width = IconOnlyWidthDip;
        _hasAnchor = false;

        if (IsVisible)
        {
            Hide();
        }
    }

    private void EnsureSpectrogram()
    {
        if (_spectrogram is not null)
        {
            return;
        }

        _spectrogram = new WriteableBitmap(
            new PixelSize(SpecWidth, SpecHeight),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using (var fb = _spectrogram.Lock())
        {
            _stride = fb.RowBytes;
            _pixels = new byte[_stride * SpecHeight];
        }

        SpectrogramImage.Source = _spectrogram;
    }

    private void ClearSpectrogram()
    {
        if (_pixels is not null)
        {
            Array.Clear(_pixels);
        }

        if (_spectrogram is null)
        {
            return;
        }

        using var fb = _spectrogram.Lock();
        FlushPixels(fb.Address);
        SpectrogramImage.InvalidateVisual();
    }

    private void FlushPixels(IntPtr address)
    {
        if (_pixels is null)
        {
            return;
        }

        Marshal.Copy(_pixels, 0, address, _pixels.Length);
    }

    private void PositionNearCursor(int cursorX, int cursorY)
    {
        if (!IsVisible)
        {
            return;
        }

        var screen = Screens.ScreenFromPoint(new PixelPoint(cursorX, cursorY))
            ?? Screens.Primary;
        if (screen is null)
        {
            Position = new PixelPoint(cursorX, cursorY);
            return;
        }

        var scaling = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
        var offsetXPx = (int)Math.Round(OffsetXDip * scaling);
        var offsetYPx = (int)Math.Round(OffsetYDip * scaling);
        var widthPx = (int)Math.Round(_layoutWidthDip * scaling);
        var heightPx = (int)Math.Round(IconSizeDip * scaling);

        var x = cursorX + offsetXPx;
        var y = cursorY + offsetYPx;

        var area = screen.WorkingArea;
        x = Math.Clamp(x, area.X, area.X + Math.Max(0, area.Width - widthPx));
        y = Math.Clamp(y, area.Y, area.Y + Math.Max(0, area.Height - heightPx));

        Position = new PixelPoint(x, y);
    }
}
