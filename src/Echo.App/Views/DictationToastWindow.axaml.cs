using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace echo.App.Views;

public partial class DictationToastWindow : Window
{
    private const int MarginDip = 16;
    private string _fullText = string.Empty;
    private Func<string, Task>? _onCopyRequested;

    public DictationToastWindow()
    {
        InitializeComponent();
    }

    public void Present(string text, Func<string, Task> onCopyRequested)
    {
        _fullText = text;
        _onCopyRequested = onCopyRequested;
        PreviewText.Text = text;

        if (!IsVisible)
        {
            Show();
        }

        Dispatcher.UIThread.Post(PositionToBottomRight, DispatcherPriority.Loaded);
    }

    public void Dismiss()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    private async void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_onCopyRequested is null || string.IsNullOrEmpty(_fullText))
        {
            return;
        }

        e.Handled = true;
        var handler = _onCopyRequested;
        var text = _fullText;
        await handler(text).ConfigureAwait(true);
    }

    private void PositionToBottomRight()
    {
        if (!IsVisible)
        {
            return;
        }

        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var scaling = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
        var widthDip = Bounds.Width > 1 ? Bounds.Width : Width;
        var heightDip = Bounds.Height > 1 ? Bounds.Height : MinHeight;
        if (widthDip <= 0)
        {
            widthDip = 360;
        }

        if (heightDip <= 0)
        {
            heightDip = 72;
        }

        var marginPx = (int)Math.Round(MarginDip * scaling);
        var widthPx = (int)Math.Round(widthDip * scaling);
        var heightPx = (int)Math.Round(heightDip * scaling);
        Position = new PixelPoint(
            area.X + area.Width - widthPx - marginPx,
            area.Y + area.Height - heightPx - marginPx);
    }
}
