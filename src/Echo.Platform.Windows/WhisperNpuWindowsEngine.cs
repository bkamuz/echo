using echo.Abstractions.Core;
using echo.Abstractions.Engines;
using echo.Abstractions.Platform;
using echo.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace echo.Platform.Windows;

/// <summary>
/// Whisper Turbo on the Snapdragon NPU via an isolated QNN worker (own ORT 1.30 / QAIRT 2.50 runtime).
/// On any NPU failure the session falls back to sherpa-onnx Whisper large-v3-turbo on CPU.
/// </summary>
public sealed class WhisperNpuWindowsEngine : ITranscriptionEngine, IDisposable
{
    public const string FallbackWhisperSize = "large-v3-turbo";

    private readonly IServiceProvider _services;
    private readonly ILogger _logger;
    private readonly IUserStatusNotifier? _statusNotifier;
    private readonly Func<ILogger, IWhisperNpuWorker> _workerFactory;
    private readonly Func<CancellationToken, Task> _ensureFallbackModel;
    private IWhisperNpuWorker? _worker;
    private ITranscriptionEngine? _cpuFallback;
    private EngineOptions _options = new();
    private bool _workerFailed;

    public WhisperNpuWindowsEngine(IServiceProvider services, ILogger logger)
        : this(services, logger, l => new WorkerAdapter(new ParakeetQnnWorkerClient(l, ParakeetAssemblyLoader.WhisperNpuEngineId)))
    {
    }

    internal WhisperNpuWindowsEngine(
        IServiceProvider services,
        ILogger logger,
        Func<ILogger, IWhisperNpuWorker> workerFactory,
        Func<CancellationToken, Task>? ensureFallbackModel = null)
    {
        _ensureFallbackModel = ensureFallbackModel ?? EnsureFallbackModelDownloadedAsync;
        _services = services;
        _logger = logger;
        _workerFactory = workerFactory;
        _statusNotifier = services.GetService(typeof(IUserStatusNotifier)) as IUserStatusNotifier;
    }

    public string EngineId => ParakeetAssemblyLoader.WhisperNpuEngineId;

    public string DisplayName => _cpuFallback is not null ? "Whisper Turbo (CPU fallback)" : "Whisper Turbo (NPU)";

    public bool UsingCpuFallback => _cpuFallback is not null;

    public void Configure(EngineOptions options)
    {
        var changed = !string.Equals(options.Language, _options.Language, StringComparison.Ordinal)
                      || !string.Equals(options.Device, _options.Device, StringComparison.Ordinal);
        _options = options;
        if (changed)
        {
            Unload();
            _workerFailed = false;
        }
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (!_workerFailed)
        {
            try
            {
                _worker ??= _workerFactory(_logger);
                await _worker.EnsureReadyAsync(_options, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await FallBackToCpuAsync(ex, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        await EnsureCpuFallbackAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> TranscribeAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
    {
        if (_worker is not null && !_workerFailed)
        {
            try
            {
                return await _worker.TranscribeAsync(samples, sampleRate, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await FallBackToCpuAsync(ex, cancellationToken).ConfigureAwait(false);
            }
        }

        if (_cpuFallback is null)
        {
            await EnsureCpuFallbackAsync(cancellationToken).ConfigureAwait(false);
        }

        return await _cpuFallback!.TranscribeAsync(samples, sampleRate, cancellationToken).ConfigureAwait(false);
    }

    public void Unload()
    {
        _worker?.Unload();
        _worker = null;
        _cpuFallback?.Unload();
        _cpuFallback = null;
    }

    public void Dispose() => Unload();

    private async Task FallBackToCpuAsync(Exception ex, CancellationToken cancellationToken)
    {
        _logger.LogWarning(ex, "Whisper NPU unavailable; falling back to sherpa Whisper {Size} on CPU for this session", FallbackWhisperSize);
        _workerFailed = true;
        _statusNotifier?.ShowTemporary("Loc.Status.WhisperNpuCpuFallback", warning: true);
        _worker?.Dispose();
        _worker = null;
        await EnsureCpuFallbackAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureCpuFallbackAsync(CancellationToken cancellationToken)
    {
        if (_cpuFallback is not null)
        {
            return;
        }

        await _ensureFallbackModel(cancellationToken).ConfigureAwait(false);

        var registration = _services.GetServices<EngineRegistration>()
            .FirstOrDefault(r => string.Equals(r.EngineId, "whisper", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Sherpa Whisper engine is not registered.");
        var engine = registration.Factory(_services);
        engine.Configure(CpuFallbackOptions(_options));
        await engine.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        _cpuFallback = engine;
        _logger.LogInformation("Whisper CPU fallback ready ({Size}, language={Language})", FallbackWhisperSize, _options.Language);
    }

    private async Task EnsureFallbackModelDownloadedAsync(CancellationToken cancellationToken)
    {
        var spec = ModelRegistry.WhisperSpec(FallbackWhisperSize);
        if (spec.IsDownloaded())
        {
            return;
        }

        var downloader = _services.GetService<ModelDownloader>()
            ?? throw new InvalidOperationException("Whisper CPU fallback model is not downloaded.");
        _logger.LogInformation("Downloading Whisper {Size} for CPU fallback", FallbackWhisperSize);
        await downloader.DownloadAsync(spec, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    internal static EngineOptions CpuFallbackOptions(EngineOptions options) => new()
    {
        Engine = "whisper",
        WhisperModelSize = FallbackWhisperSize,
        GigaAmModelSize = options.GigaAmModelSize,
        Language = options.Language,
        Device = "cpu",
        SampleRate = options.SampleRate,
    };

    internal interface IWhisperNpuWorker : IDisposable
    {
        Task EnsureReadyAsync(EngineOptions options, CancellationToken cancellationToken);
        Task<string> TranscribeAsync(float[] samples, int sampleRate, CancellationToken cancellationToken);
        void Unload();
    }

    private sealed class WorkerAdapter(ParakeetQnnWorkerClient client) : IWhisperNpuWorker
    {
        public Task EnsureReadyAsync(EngineOptions options, CancellationToken cancellationToken) =>
            client.EnsureReadyAsync(options, cancellationToken);

        public Task<string> TranscribeAsync(float[] samples, int sampleRate, CancellationToken cancellationToken) =>
            client.TranscribeAsync(samples, sampleRate, cancellationToken);

        public void Unload() => client.Unload();

        public void Dispose() => client.Dispose();
    }
}
