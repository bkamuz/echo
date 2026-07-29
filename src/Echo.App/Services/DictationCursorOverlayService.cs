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
    private readonly IAudioCapture _audio;
    private readonly Bitmap _recordingIcon;
    private readonly Bitmap _processingIcon;
    private readonly float[] _latestBands = new float[AudioLevelMeter.BandCount];
    private DictationCursorOverlayWindow? _window;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private EventHandler<ControlledApplicationLifetimeExitEventArgs>? _exitHandler;
    private DictationOverlayState _state = DictationOverlayState.Hidden;
    private bool _spectrumUpdatePosted;
    private bool _listeningToSpectrum;
    private bool _disposed;

    public DictationCursorOverlayService(ICursorPosition cursor, IAudioCapture audio)
    {
        _cursor = cursor;
        _audio = audio;
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

        _state = state;

        switch (state)
        {
            case DictationOverlayState.Hidden:
                StopListeningToSpectrum();
                _window?.Dismiss();
                break;

            case DictationOverlayState.Recording:
                if (TryShow(_recordingIcon, showMeter: true))
                {
                    StartListeningToSpectrum();
                }

                break;

            case DictationOverlayState.Processing:
                StopListeningToSpectrum();
                if (_window is { IsVisible: true })
                {
                    _window.UpdateIcon(_processingIcon);
                    _window.SetMeterVisible(false);
                }
                else
                {
                    TryShow(_processingIcon, showMeter: false);
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }

    private bool TryShow(Bitmap icon, bool showMeter)
    {
        if (!_cursor.TryGetPosition(out var x, out var y))
        {
            return false;
        }

        if (!EnsureWindow())
        {
            return false;
        }

        _window!.Present(icon, x, y, showMeter);
        return true;
    }

    private void StartListeningToSpectrum()
    {
        if (_listeningToSpectrum)
        {
            return;
        }

        _listeningToSpectrum = true;
        _audio.SpectrumChanged += OnSpectrumChanged;
    }

    private void StopListeningToSpectrum()
    {
        if (!_listeningToSpectrum)
        {
            return;
        }

        _listeningToSpectrum = false;
        _audio.SpectrumChanged -= OnSpectrumChanged;
        Array.Clear(_latestBands);
        _spectrumUpdatePosted = false;
    }

    private void OnSpectrumChanged(object? sender, float[] bands)
    {
        if (_disposed || _state != DictationOverlayState.Recording)
        {
            return;
        }

        var n = Math.Min(bands.Length, _latestBands.Length);
        Array.Copy(bands, _latestBands, n);
        if (_spectrumUpdatePosted)
        {
            return;
        }

        _spectrumUpdatePosted = true;
        Dispatcher.UIThread.Post(FlushSpectrumToUi);
    }

    private void FlushSpectrumToUi()
    {
        _spectrumUpdatePosted = false;
        if (_disposed || _state != DictationOverlayState.Recording)
        {
            return;
        }

        _window?.UpdateSpectrum(_latestBands);
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
        StopListeningToSpectrum();

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
