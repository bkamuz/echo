using echo.Abstractions.Engines;
using echo.Engines.ParakeetNpu.Parakeet;
using Microsoft.Extensions.Logging;

namespace echo.Engines.ParakeetNpu;

public sealed class ParakeetNpuEngine : ITranscriptionEngine, IDisposable
{
    private readonly QnnRuntimeDownloader _runtimeDownloader;
    private readonly ParakeetModelDownloader _modelDownloader;
    private readonly ILogger<ParakeetNpuEngine> _logger;

    private ParakeetPipeline? _pipeline;
    private string _resolvedProvider = "cpu";

    public ParakeetNpuEngine(
        QnnRuntimeDownloader runtimeDownloader,
        ParakeetModelDownloader modelDownloader,
        ILogger<ParakeetNpuEngine> logger)
    {
        _runtimeDownloader = runtimeDownloader;
        _modelDownloader = modelDownloader;
        _logger = logger;
    }

    public string EngineId => "parakeet_npu";

    public string DisplayName => "Parakeet NPU (experimental)";

    public string ResolvedProvider => _resolvedProvider;

    public void Configure(EngineOptions options)
    {
        // Parakeet NPU ignores Sherpa device strings; always uses ORT QNN when loaded.
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_pipeline is not null)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Parakeet NPU is Windows-only.");
        }

        SnapdragonHardware.EnsureSnapdragonXElite();

        await _runtimeDownloader.EnsureInstalledAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await _modelDownloader.EnsureInstalledAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var modelDir = ParakeetModelDownloader.ModelDir;
        var runtimeDir = QnnRuntimeDownloader.RuntimeDir;

        try
        {
            _logger.LogInformation(
                "Loading Parakeet NPU pipeline (provider=qnn/htp, model={ModelDir})",
                modelDir);
            _pipeline = ParakeetPipeline.LoadNpu(modelDir, runtimeDir, _logger);
            _resolvedProvider = "qnn/htp";
            _logger.LogInformation("Parakeet NPU ready — encoder on Hexagon HTP via ORT QNN EP");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Parakeet NPU (HTP) load failed");
            throw new InvalidOperationException(
                "Could not load Parakeet on the Hexagon NPU. Check echo.log for QNN/HTP details " +
                "(missing skel/cat, wrong SoC, or incomplete runtime).", ex);
        }
    }

    public Task<string> TranscribeAsync(
        float[] samples,
        int sampleRate,
        CancellationToken cancellationToken = default)
    {
        if (_pipeline is null)
        {
            throw new InvalidOperationException("Parakeet NPU model is not loaded.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var text = _pipeline.Transcribe(samples, sampleRate, _logger);
        _logger.LogInformation(
            "Parakeet transcribe complete provider={Provider} chars={Chars}",
            _resolvedProvider,
            text.Length);
        return Task.FromResult(text);
    }

    public void Unload()
    {
        _pipeline?.Dispose();
        _pipeline = null;
        _resolvedProvider = "cpu";
    }

    public void Dispose() => Unload();
}
