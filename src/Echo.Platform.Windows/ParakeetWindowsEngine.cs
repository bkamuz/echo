using echo.Abstractions.Engines;
using echo.Abstractions.Platform;
using Microsoft.Extensions.Logging;

namespace echo.Platform.Windows;

/// <summary>
/// Routes Parakeet CPU in-process and Parakeet NPU through an isolated QNN worker on Windows ARM64.
/// </summary>
public sealed class ParakeetWindowsEngine : ITranscriptionEngine, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly ILogger _logger;
    private readonly IUserStatusNotifier? _statusNotifier;
    private readonly ParakeetNpuWorkerSessionGate _workerGate = new();
    private ITranscriptionEngine? _inProcess;
    private ParakeetQnnWorkerClient? _npuWorker;
    private EngineOptions _options = new();
    private bool _loadedViaWorker;

    public ParakeetWindowsEngine(IServiceProvider services, ILogger logger)
    {
        _services = services;
        _logger = logger;
        _statusNotifier = services.GetService(typeof(IUserStatusNotifier)) as IUserStatusNotifier;
    }

    public string EngineId => "parakeet_npu";

    public string DisplayName =>
        _loadedViaWorker
            ? (_npuWorker?.LoadedDisplayName ?? "Parakeet NPU (experimental)")
            : (_inProcess?.DisplayName ?? "Parakeet NPU (experimental)");

    public void Configure(EngineOptions options)
    {
        var previous = _options;
        var previousNpu = ParakeetQnnWorkerPolicy.IsNpuDevice(previous.Device);
        _options = CloneOptions(options);
        var wantNpu = ParakeetQnnWorkerPolicy.IsNpuDevice(_options.Device);
        var optionsChanged = !OptionsEqual(previous, _options);

        _workerGate.OnConfigure(wantNpu, optionsChanged);

        if (previousNpu != wantNpu)
        {
            Unload();
        }
        else if (!wantNpu && _inProcess is not null)
        {
            _inProcess.Configure(_options);
        }
        else if (wantNpu && optionsChanged && _loadedViaWorker)
        {
            Unload();
        }
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        var wantNpu = ParakeetQnnWorkerPolicy.IsNpuDevice(_options.Device);
        if (_workerGate.ShouldUseWorker(wantNpu, ParakeetQnnWorkerPolicy.ShouldIsolateNpu()))
        {
            _inProcess?.Unload();
            _inProcess = null;

            try
            {
                _npuWorker ??= new ParakeetQnnWorkerClient(_logger);
                await _npuWorker.EnsureReadyAsync(_options, cancellationToken).ConfigureAwait(false);
                _loadedViaWorker = true;
                return;
            }
            catch (Exception ex)
            {
                await FallBackToCpuForSessionAsync(ex, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        _npuWorker?.Dispose();
        _npuWorker = null;
        _loadedViaWorker = false;

        _inProcess ??= ParakeetAssemblyLoader.CreateInProcessEngine(_services);
        _inProcess.Configure(ResolveInProcessOptions(wantNpu));
        await _inProcess.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> TranscribeAsync(
        float[] samples,
        int sampleRate,
        CancellationToken cancellationToken = default)
    {
        if (_loadedViaWorker)
        {
            if (_npuWorker is null)
            {
                throw new InvalidOperationException("Parakeet NPU worker is not loaded.");
            }

            try
            {
                return await _npuWorker.TranscribeAsync(samples, sampleRate, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await FallBackToCpuForSessionAsync(ex, cancellationToken).ConfigureAwait(false);
            }
        }

        if (_inProcess is null)
        {
            throw new InvalidOperationException("Parakeet model is not loaded.");
        }

        return await _inProcess.TranscribeAsync(samples, sampleRate, cancellationToken).ConfigureAwait(false);
    }

    public void Unload()
    {
        _inProcess?.Unload();
        _inProcess = null;
        _npuWorker?.Unload();
        _npuWorker = null;
        _loadedViaWorker = false;
    }

    public void Dispose() => Unload();

    private async Task FallBackToCpuForSessionAsync(Exception ex, CancellationToken cancellationToken)
    {
        _logger.LogWarning(ex, "Parakeet NPU worker unavailable; using in-process CPU for this session");
        _workerGate.MarkWorkerFailed();
        _statusNotifier?.ShowTemporary("Loc.Status.ParakeetNpuCpuFallback", warning: true);

        _npuWorker?.Dispose();
        _npuWorker = null;
        _loadedViaWorker = false;

        _inProcess ??= ParakeetAssemblyLoader.CreateInProcessEngine(_services);
        _inProcess.Configure(CpuFallbackOptions());
        await _inProcess.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
    }

    private EngineOptions ResolveInProcessOptions(bool wantNpu) =>
        wantNpu ? CpuFallbackOptions() : _options;

    private EngineOptions CpuFallbackOptions() => new()
    {
        Engine = _options.Engine,
        WhisperModelSize = _options.WhisperModelSize,
        GigaAmModelSize = _options.GigaAmModelSize,
        Language = _options.Language,
        Device = "cpu",
        SampleRate = _options.SampleRate,
    };

    private static EngineOptions CloneOptions(EngineOptions options) => new()
    {
        Engine = options.Engine,
        WhisperModelSize = options.WhisperModelSize,
        GigaAmModelSize = options.GigaAmModelSize,
        Language = options.Language,
        Device = options.Device,
        SampleRate = options.SampleRate,
    };

    private static bool OptionsEqual(EngineOptions left, EngineOptions right) =>
        string.Equals(left.Engine, right.Engine, StringComparison.Ordinal)
        && string.Equals(left.WhisperModelSize, right.WhisperModelSize, StringComparison.Ordinal)
        && string.Equals(left.GigaAmModelSize, right.GigaAmModelSize, StringComparison.Ordinal)
        && string.Equals(left.Language, right.Language, StringComparison.Ordinal)
        && string.Equals(left.Device, right.Device, StringComparison.Ordinal)
        && left.SampleRate == right.SampleRate;
}
