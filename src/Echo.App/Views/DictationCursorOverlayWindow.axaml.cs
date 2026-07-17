using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace echo.App.Views;

public partial class DictationCursorOverlayWindow : Window
{
    private const double OffsetXDip = 12;
    private const double OffsetYDip = -28;
    private const double SizeDip = 24;

    public DictationCursorOverlayWindow()
    {
        InitializeComponent();
    }

    public void Present(Bitmap icon, int cursorX, int cursorY)
    {
        IconImage.Source = icon;

        if (!IsVisible)
        {
            Show();
        }

        PositionNearCursor(cursorX, cursorY);
    }

    public void UpdateIcon(Bitmap icon) => IconImage.Source = icon;

    public void Dismiss()
    {
        if (IsVisible)
        {
            Hide();
        }
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
        var sizePx = (int)Math.Round(SizeDip * scaling);

        var x = cursorX + offsetXPx;
        var y = cursorY + offsetYPx;

        var area = screen.WorkingArea;
        x = Math.Clamp(x, area.X, area.X + Math.Max(0, area.Width - sizePx));
        y = Math.Clamp(y, area.Y, area.Y + Math.Max(0, area.Height - sizePx));

        Position = new PixelPoint(x, y);
    }
}
