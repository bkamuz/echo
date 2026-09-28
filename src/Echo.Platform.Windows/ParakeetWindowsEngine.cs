using echo.Abstractions.Engines;
using Microsoft.Extensions.Logging;

namespace echo.Platform.Windows;

/// <summary>
/// Routes Parakeet CPU in-process and Parakeet NPU through an isolated QNN worker on Windows ARM64.
/// </summary>
public sealed class ParakeetWindowsEngine : ITranscriptionEngine, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly ILogger _logger;
    private ITranscriptionEngine? _inProcess;
    private ParakeetQnnWorkerClient? _npuWorker;
    private EngineOptions _options = new();
    private bool _loadedViaWorker;

    public ParakeetWindowsEngine(IServiceProvider services, ILogger logger)
    {
        _services = services;
        _logger = logger;
    }

    public string EngineId => "parakeet_npu";

    public string DisplayName =>
        _loadedViaWorker
            ? (_npuWorker?.LoadedDisplayName ?? "Parakeet NPU (experimental)")
            : (_inProcess?.DisplayName ?? "Parakeet NPU (experimental)");

    public void Configure(EngineOptions options)
    {
        var previousNpu = ParakeetQnnWorkerPolicy.IsNpuDevice(_options.Device);
        _options = CloneOptions(options);
        var wantNpu = ParakeetQnnWorkerPolicy.IsNpuDevice(_options.Device);

        if (previousNpu != wantNpu)
        {
            Unload();
        }
        else if (!wantNpu && _inProcess is not null)
        {
            _inProcess.Configure(_options);
        }
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        var wantNpu = ParakeetQnnWorkerPolicy.IsNpuDevice(_options.Device);
        if (wantNpu && ParakeetQnnWorkerPolicy.ShouldIsolateNpu())
        {
            _inProcess?.Unload();
            _inProcess = null;

            _npuWorker ??= new ParakeetQnnWorkerClient(_logger);
            await _npuWorker.EnsureReadyAsync(_options, cancellationToken).ConfigureAwait(false);
            _loadedViaWorker = true;
            return;
        }

        _npuWorker?.Dispose();
        _npuWorker = null;
        _loadedViaWorker = false;

        _inProcess ??= ParakeetAssemblyLoader.CreateInProcessEngine(_services);
        _inProcess.Configure(_options);
        await _inProcess.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<string> TranscribeAsync(
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

            return _npuWorker.TranscribeAsync(samples, sampleRate, cancellationToken);
        }

        if (_inProcess is null)
        {
            throw new InvalidOperationException("Parakeet model is not loaded.");
        }

        return _inProcess.TranscribeAsync(samples, sampleRate, cancellationToken);
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

    private static EngineOptions CloneOptions(EngineOptions options) => new()
    {
        Engine = options.Engine,
        WhisperModelSize = options.WhisperModelSize,
        GigaAmModelSize = options.GigaAmModelSize,
        Language = options.Language,
        Device = options.Device,
        SampleRate = options.SampleRate,
    };
}
