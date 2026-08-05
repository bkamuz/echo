using echo.Abstractions.Core;
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
    private readonly IDictationResultNotifier _dictationToast;
    private readonly IUserStatusNotifier? _statusNotifier;
    private readonly ILogger<DictationCoordinator> _logger;

    private AppConfig _config;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private int _modelBusyCount;
    private DateTimeOffset _pressStarted;
    private bool _isRecording;
    private nint _targetWindow;
    private nint _targetFocus;

    public DictationCoordinator(
        ConfigStore configStore,
        TranscriptionService transcription,
        HistoryStore history,
        IAudioCapture audio,
        IHotkeyService hotkey,
        ITextInjector injector,
        IFocusTarget focusTarget,
        ITrayStateService tray,
        IDictationResultNotifier dictationToast,
        ILogger<DictationCoordinator> logger,
        IUserStatusNotifier? statusNotifier = null)
    {
        _configStore = configStore;
        _transcription = transcription;
        _history = history;
        _audio = audio;
        _hotkey = hotkey;
        _injector = injector;
        _focusTarget = focusTarget;
        _tray = tray;
        _dictationToast = dictationToast;
        _statusNotifier = statusNotifier;
        _logger = logger;
        _config = _configStore.Load();
    }

    public AppConfig Config => _config;

    public string? LastOutcomeMessage { get; private set; }

    /// <summary>
    /// True while a model is downloading or loading into memory — dictation must not start.
    /// </summary>
    public bool IsModelBusy => Volatile.Read(ref _modelBusyCount) > 0;

    public event Action? OutcomeChanged;

    /// <summary>
    /// Marks model download/warmup as in progress so the hold-to-dictate hotkey is ignored.
    /// </summary>
    public IDisposable EnterModelBusy()
    {
        if (Interlocked.Increment(ref _modelBusyCount) == 1)
        {
            // Drop any in-flight capture — Stop() will not raise Deactivated.
            AbortActiveRecording();
            // Fully mute the global hotkey so presses cannot overwrite download status.
            _hotkey.Stop();
        }

        return new ModelBusyScope(this);
    }

    public void ReloadConfig()
    {
        _config = _configStore.Load();
        _hotkey.Configure(_config.Hotkey);
    }

    public async Task SaveConfigAsync(
        AppConfig config,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var incoming = config.Clone();
        incoming.Normalize();
        var needsEngineWork = RequiresEngineWarmup(_config, incoming);

        // Mic / input-method / toast toggles must not stop the hotkey or reload the model.
        using var modelBusy = needsEngineWork ? EnterModelBusy() : null;
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(ProgressMessages.Saving());

            var previousInputDevice = _config.InputDevice;
            _config = incoming;
            _configStore.Save(_config);
            _hotkey.Configure(_config.Hotkey);

            if (!string.Equals(previousInputDevice, _config.InputDevice, StringComparison.Ordinal))
            {
                AbortActiveRecording();
            }

            if (!needsEngineWork)
            {
                return;
            }

            try
            {
                await WarmupIfDownloadedAsync(_config, progress, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Model warmup failed");
                throw;
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private static bool RequiresEngineWarmup(AppConfig current, AppConfig next) =>
        !string.Equals(current.Engine, next.Engine, StringComparison.Ordinal)
        || !string.Equals(current.WhisperModelSize, next.WhisperModelSize, StringComparison.Ordinal)
        || !string.Equals(current.GigaAmModelSize, next.GigaAmModelSize, StringComparison.Ordinal)
        || !string.Equals(current.Language, next.Language, StringComparison.Ordinal)
        || !string.Equals(current.Device, next.Device, StringComparison.Ordinal)
        || current.SampleRate != next.SampleRate;

    public void Start()
    {
        ReloadConfig();
        _hotkey.Configure(_config.Hotkey);
        _hotkey.Activated += OnHotkeyActivated;
        _hotkey.Deactivated += () => _ = HandleDeactivatedAsync();
        _hotkey.Start();
        if (_hotkey.IsActive)
        {
            _logger.LogInformation("Hotkey active: {Hotkey}", _config.Hotkey);
        }
        else
        {
            _logger.LogWarning("Global hotkey is not listening — check Linux input group or hotkey permissions");
        }
        _ = WarmupModelAsync();
    }

    private async Task WarmupModelAsync()
    {
        try
        {
            using (EnterModelBusy())
            {
                if (!await WarmupIfDownloadedAsync(_config))
                {
                    _logger.LogInformation("Model not downloaded — skipping startup warmup");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Model warmup failed — first dictation may be slower");
        }
    }

    public async Task TryWarmupCurrentModelAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var busy = EnterModelBusy();
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            await WarmupIfDownloadedAsync(_config, progress, cancellationToken);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private async Task<bool> WarmupIfDownloadedAsync(
        AppConfig config,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!ModelRegistry.IsEngineModelDownloaded(config.Engine, config.WhisperModelSize, config.GigaAmModelSize))
        {
            return false;
        }

        progress?.Report(ProgressMessages.LoadingModel());
        cancellationToken.ThrowIfCancellationRequested();

        // Nested with TryWarmup/startup EnterModelBusy — keeps SaveConfigAsync covered too.
        using var busy = EnterModelBusy();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _transcription.WarmupAsync(config, cancellationToken);
        _logger.LogInformation("Model warmup completed in {Ms} ms", sw.ElapsedMilliseconds);
        return true;
    }

    public void Stop()
    {
        _hotkey.Activated -= OnHotkeyActivated;
        _hotkey.Stop();
        AbortActiveRecording();
    }

    private void AbortActiveRecording()
    {
        if (!_isRecording)
        {
            return;
        }

        _isRecording = false;
        _targetWindow = 0;
        _targetFocus = 0;
        try
        {
            _audio.StopRecording();
        }
        catch
        {
            // Ignore audio stop failures during abort/shutdown.
        }

        _tray.SetState(DictationOverlayState.Hidden);
    }

    public void RestartHotkey()
    {
        _hotkey.Stop();
        _hotkey.Configure(_config.Hotkey);
        _hotkey.Start();
        if (_hotkey.IsActive)
        {
            _logger.LogInformation("Hotkey active: {Hotkey}", _config.Hotkey);
        }
        else
        {
            _logger.LogWarning("Global hotkey is not listening — check Linux input group or hotkey permissions");
        }
    }

    private void OnHotkeyActivated()
    {
        if (_isRecording || IsModelBusy)
        {
            return;
        }

        _isRecording = true;
        _pressStarted = DateTimeOffset.UtcNow;
        _targetWindow = CaptureInjectionTarget();
        try
        {
            _audio.StartRecording(_config.SampleRate, _config.InputDevice);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start audio recording");
            _isRecording = false;
            FinishDictation("Не удалось начать запись микрофона", alert: true);
            return;
        }

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
            FinishDictation(status: null);
            return;
        }

        // Switch UI immediately; safe WASAPI stop runs off the UI thread so the
        // listening icon/spectrogram does not linger during RecordingStopped wait.
        _tray.SetState(DictationOverlayState.Processing);

        // Only undo if Echo itself stole activation. Unconditional restore
        // (ShowWindow/SetForegroundWindow) blurs caret in browser chats.
        RestoreInjectionTarget(onlyIfStolenByUs: true);

        await Task.Yield();

        float[] samples;
        try
        {
            // Stop on a worker thread so RecordingStopped wait does not block the UI,
            // but resume on the UI sync context for Avalonia tray/overlay updates.
            samples = await Task.Run(_audio.StopRecording);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop audio recording");
            FinishDictation("Loc.Status.RecognitionError", alert: true);
            return;
        }

        try
        {
            if (samples.Length == 0)
            {
                FinishDictation("Тишина — речь не распознана", warning: true);
                return;
            }

            if (!ModelRegistry.IsEngineModelDownloaded(
                    _config.Engine,
                    _config.WhisperModelSize,
                    _config.GigaAmModelSize))
            {
                FinishDictation("Модель не загружена — скачайте в «Настройках»", alert: true);
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var text = await Task.Run(async () =>
                await _transcription.TranscribeAsync(_config, samples).ConfigureAwait(false));
            var transcribeMs = sw.ElapsedMilliseconds;

            if (string.IsNullOrWhiteSpace(text))
            {
                FinishDictation("Речь не распознана", warning: true);
                return;
            }

            if (_config.AddTrailingSpace && !text.EndsWith(' '))
            {
                text += " ";
            }

            var engine = _transcription.Resolve(_config);
            _history.Append(engine.DisplayName, text.TrimEnd());

            sw.Restart();
            // Drop the processing overlay before paste so it cannot sit as the
            // foreground HWND and steal Ctrl+V from browser chat inputs.
            _tray.SetState(DictationOverlayState.Hidden);
            RestoreInjectionTarget();
            try
            {
                var injectResult = await _injector.InjectAsync(text, _config.InputMethod, _config.TypeDelayMs);
                var injectMs = sw.ElapsedMilliseconds;
                if (injectResult.Outcome == TextInjectionOutcome.Failed)
                {
                    _logger.LogWarning(
                        "Text injection failed — result saved to history: {Message}",
                        injectResult.Message);
                    FinishDictation(
                        injectResult.Message ?? "Loc.Inject.Failed",
                        alert: true);
                }
                else if (injectResult.Outcome == TextInjectionOutcome.ClipboardOnly)
                {
                    _logger.LogInformation(
                        "Dictation done (clipboard only): transcribe={TranscribeMs}ms inject={InjectMs}ms chars={Chars} — {Message}",
                        transcribeMs,
                        injectMs,
                        text.Length,
                        injectResult.Message);
                    FinishDictation(
                        injectResult.Message ?? "Loc.Linux.Inject.ClipboardOnly",
                        warning: true);
                }
                else
                {
                    _logger.LogInformation(
                        "Dictation done: transcribe={TranscribeMs}ms inject={InjectMs}ms chars={Chars}",
                        transcribeMs,
                        injectMs,
                        text.Length);
                    FinishDictation(status: null);
                }
            }
            catch (Exception injectEx)
            {
                _logger.LogWarning(injectEx, "Text injection failed — result saved to history");
                FinishDictation("Loc.Inject.Failed", alert: true);
            }

            _dictationToast.Show(text.TrimEnd());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transcription pipeline failed");
            var message = ex is InvalidOperationException
                ? ex.Message
                : "Loc.Status.RecognitionError";
            FinishDictation(message, alert: true);
        }
        finally
        {
            _targetWindow = 0;
            _targetFocus = 0;
            _tray.SetState(DictationOverlayState.Hidden);
        }
    }

    private void FinishDictation(string? status, bool alert = false, bool warning = false)
    {
        _tray.SetState(DictationOverlayState.Hidden);
        LastOutcomeMessage = status;
        OutcomeChanged?.Invoke();

        if (!string.IsNullOrEmpty(status))
        {
            _statusNotifier?.ShowTemporary(status, alert: alert, warning: warning);
        }
    }

    private nint CaptureInjectionTarget()
    {
        var handle = _focusTarget.CaptureTargetWindow();
        _targetFocus = handle == 0 || _focusTarget.IsOwnWindow(handle)
            ? 0
            : _focusTarget.CaptureTargetFocus();

        if (handle == 0 || _focusTarget.IsOwnWindow(handle))
        {
            return 0;
        }

        return handle;
    }

    private void RestoreInjectionTarget(bool onlyIfStolenByUs = false)
    {
        if (_targetWindow == 0 && _targetFocus == 0)
        {
            return;
        }

        var foreground = _focusTarget.CaptureTargetWindow();
        var needsForegroundRestore = _targetWindow != 0 && foreground != _targetWindow;

        if (needsForegroundRestore)
        {
            if (onlyIfStolenByUs && (foreground == 0 || !_focusTarget.IsOwnWindow(foreground)))
            {
                return;
            }

            _focusTarget.RestoreTargetWindow(_targetWindow);
            Thread.Sleep(30);
        }

        // Even when the top-level HWND stayed foreground (Chromium chats), overlays can
        // move keyboard focus away from the contenteditable — restore caret before paste.
        if (!onlyIfStolenByUs && _targetFocus != 0)
        {
            _focusTarget.RestoreTargetFocus(_targetFocus);
            Thread.Sleep(10);
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private sealed class ModelBusyScope(DictationCoordinator owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (Interlocked.Decrement(ref owner._modelBusyCount) == 0)
            {
                owner._hotkey.Configure(owner._config.Hotkey);
                owner._hotkey.Start();
            }
        }
    }
}
