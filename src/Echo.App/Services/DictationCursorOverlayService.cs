using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using echo.Abstractions.Platform;
using echo.App.Views;

namespace echo.App.Services;

public sealed class DictationCursorOverlayService : IDisposable
{
    private readonly ICursorPosition _cursor;
    private readonly Bitmap _recordingIcon;
    private readonly Bitmap _processingIcon;
    private DictationCursorOverlayWindow? _window;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private EventHandler<ControlledApplicationLifetimeExitEventArgs>? _exitHandler;
    private bool _disposed;

    public DictationCursorOverlayService(ICursorPosition cursor)
    {
        _cursor = cursor;
        _recordingIcon = LoadIcon("listen");
        _processingIcon = LoadIcon("processing");
    }

    public void SetState(DictationOverlayState state)
    {
        if (_disposed)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyState(state);
            return;
        }

        Dispatcher.UIThread.Post(() => ApplyState(state));
    }

    private void ApplyState(DictationOverlayState state)
    {
        if (_disposed)
        {
            return;
        }

        switch (state)
        {
            case DictationOverlayState.Hidden:
                _window?.Dismiss();
                break;

            case DictationOverlayState.Recording:
                TryShow(_recordingIcon);
                break;

            case DictationOverlayState.Processing:
                if (_window is { IsVisible: true })
                {
                    _window.UpdateIcon(_processingIcon);
                }
                else
                {
                    TryShow(_processingIcon);
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }

    private bool TryShow(Bitmap icon)
    {
        if (!_cursor.TryGetPosition(out var x, out var y))
        {
            return false;
        }

        if (!EnsureWindow())
        {
            return false;
        }

        _window!.Present(icon, x, y);
        return true;
    }

    private bool EnsureWindow()
    {
        if (_window is not null)
        {
            return true;
        }

        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return false;
        }

        _desktop = desktop;
        _window = new DictationCursorOverlayWindow();
        _exitHandler = OnDesktopExit;
        desktop.Exit += _exitHandler;
        return true;
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e) =>
        DisposeWindowResources();

    private void DisposeWindowResources()
    {
        if (_window is not null)
        {
            _window.Close();
            _window = null;
        }

        if (_desktop is not null && _exitHandler is not null)
        {
            _desktop.Exit -= _exitHandler;
            _exitHandler = null;
            _desktop = null;
        }
    }

    private static Bitmap LoadIcon(string name)
    {
        var uri = new Uri($"avares://echo.App/Resources/{name}.png");
        using var stream = AssetLoader.Open(uri);
        return new Bitmap(stream);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeWindowResources();
        _recordingIcon.Dispose();
        _processingIcon.Dispose();
    }
}
