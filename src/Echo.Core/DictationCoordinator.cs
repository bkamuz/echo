using echo.Abstractions.Platform;
using Microsoft.Extensions.Logging;

namespace echo.Core;

public sealed class DictationCoordinator : IDisposable
{
    private readonly ConfigStore _configStore;
    private readonly TranscriptionService _transcription;
    private readonly HistoryStore _history;
    private readonly IAudioCapture _audio;
    private readonly IHotkeyService _hotkey;
    private readonly ITextInjector _injector;
    private readonly IFocusTarget _focusTarget;
    private readonly ITrayStateService _tray;
    private readonly ILogger<DictationCoordinator> _logger;

    private AppConfig _config;
    private DateTimeOffset _pressStarted;
    private bool _isRecording;
    private nint _targetWindow;

    public DictationCoordinator(
        ConfigStore configStore,
        TranscriptionService transcription,
        HistoryStore history,
        IAudioCapture audio,
        IHotkeyService hotkey,
        ITextInjector injector,
        IFocusTarget focusTarget,
        ITrayStateService tray,
        ILogger<DictationCoordinator> logger)
    {
        _configStore = configStore;
        _transcription = transcription;
        _history = history;
        _audio = audio;
        _hotkey = hotkey;
        _injector = injector;
        _focusTarget = focusTarget;
        _tray = tray;
        _logger = logger;
        _config = _configStore.Load();
    }

    public AppConfig Config => _config;

    public void ReloadConfig()
    {
        _config = _configStore.Load();
        _hotkey.Configure(_config.Hotkey);
    }

    public void SaveConfig(AppConfig config)
    {
        _config = config;
        _config.Normalize();
        _configStore.Save(_config);
        _hotkey.Configure(_config.Hotkey);
        _ = WarmupModelAsync();
    }

    public void Start()
    {
        ReloadConfig();
        _hotkey.Configure(_config.Hotkey);
        _hotkey.Activated += OnHotkeyActivated;
        _hotkey.Deactivated += () => _ = HandleDeactivatedAsync();
        _hotkey.Start();
        _logger.LogInformation("Hotkey active: {Hotkey}", _config.Hotkey);
        _ = WarmupModelAsync();
    }

    private async Task WarmupModelAsync()
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await _transcription.WarmupAsync(_config);
            _logger.LogInformation("Model warmup completed in {Ms} ms", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Model warmup failed — first dictation may be slower");
        }
    }

    public void Stop()
    {
        _hotkey.Activated -= OnHotkeyActivated;
        _hotkey.Stop();
    }

    private void OnHotkeyActivated()
    {
        if (_isRecording)
        {
            return;
        }

        _isRecording = true;
        _pressStarted = DateTimeOffset.UtcNow;
        _targetWindow = CaptureInjectionTarget();
        _audio.StartRecording(_config.SampleRate, _config.InputDevice);
        _tray.SetState(DictationOverlayState.Recording);
    }

    private async Task HandleDeactivatedAsync()
    {
        if (!_isRecording)
        {
            return;
        }

        _isRecording = false;

        var elapsed = DateTimeOffset.UtcNow - _pressStarted;
        if (elapsed.TotalMilliseconds < _config.MinPressMs)
        {
            _audio.StopRecording();
            _tray.SetState(DictationOverlayState.Hidden);
            return;
        }

        _tray.SetState(DictationOverlayState.Processing);

        await Task.Yield();

        try
        {
            var samples = _audio.StopRecording();
            if (samples.Length == 0)
            {
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var text = await Task.Run(async () =>
                await _transcription.TranscribeAsync(_config, samples).ConfigureAwait(false));
            var transcribeMs = sw.ElapsedMilliseconds;

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (_config.AddTrailingSpace && !text.EndsWith(' '))
            {
                text += " ";
            }

            sw.Restart();
            _tray.SetState(DictationOverlayState.Hidden);
            RestoreInjectionTarget();
            await _injector.InjectAsync(text, _config.InputMethod);
            var injectMs = sw.ElapsedMilliseconds;

            var engine = _transcription.Resolve(_config);
            _history.Append(engine.DisplayName, text.TrimEnd());
            _logger.LogInformation(
                "Dictation done: transcribe={TranscribeMs}ms inject={InjectMs}ms chars={Chars}",
                transcribeMs,
                injectMs,
                text.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transcription pipeline failed");
        }
        finally
        {
            _targetWindow = 0;
            _tray.SetState(DictationOverlayState.Hidden);
        }
    }

    private nint CaptureInjectionTarget()
    {
        var handle = _focusTarget.CaptureTargetWindow();
        return handle == 0 ? 0 : handle;
    }

    private void RestoreInjectionTarget()
    {
        if (_targetWindow == 0)
        {
            return;
        }

        _focusTarget.RestoreTargetWindow(_targetWindow);
        Thread.Sleep(30);
    }

    public void Dispose()
    {
        Stop();
    }
}
