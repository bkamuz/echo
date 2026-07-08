using echo.Abstractions.Core;
using echo.Abstractions.Engines;
using Microsoft.Extensions.Logging;
using SherpaOnnx;

namespace echo.Engines.GigaAm;

public sealed class GigaAmEngine : ITranscriptionEngine, IDisposable
{
    private readonly ILogger<GigaAmEngine> _logger;
    private EngineOptions _config = new();
    private OfflineRecognizer? _recognizer;
    private string _resolvedDevice = "cpu";
    private bool _usesE2e;

    public GigaAmEngine(ILogger<GigaAmEngine> logger)
    {
        _logger = logger;
    }

    public string EngineId => "gigaam";
    public string DisplayName => _usesE2e
        ? $"GigaAM v3 e2e ({_resolvedDevice.ToUpperInvariant()})"
        : $"GigaAM v3 ({_resolvedDevice.ToUpperInvariant()})";

    public void Configure(EngineOptions options) => _config = options;

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_recognizer is not null)
        {
            return Task.CompletedTask;
        }

        var paths = ResolveModelPaths(AppPaths.GigaAmDir);
        if (paths is null)
        {
            throw new InvalidOperationException(
                "GigaAM модели не найдены. Скачайте GigaAM v3 через меню приложения " +
                "(для .NET нужны sherpa-совместимые ONNX из Smirnov75/GigaAM-v3-sherpa-onnx).");
        }

        _resolvedDevice = _config.Device == "cuda" ? "cuda" : "cpu";
        _usesE2e = paths.Encoder.Contains("e2e_rnnt", StringComparison.Ordinal);
        var config = new OfflineRecognizerConfig
        {
            FeatConfig = new FeatureConfig
            {
                SampleRate = _config.SampleRate,
                FeatureDim = 80,
            },
            ModelConfig = new OfflineModelConfig
            {
                Tokens = paths.Tokens,
                NumThreads = Math.Max(1, Environment.ProcessorCount),
                Provider = _resolvedDevice,
                Transducer = new OfflineTransducerModelConfig
                {
                    Encoder = paths.Encoder,
                    Decoder = paths.Decoder,
                    Joiner = paths.Joiner,
                },
            },
        };

        try
        {
            _logger.LogInformation(
                "Loading GigaAM v3{Variant} (device={Device})",
                _usesE2e ? " e2e" : string.Empty,
                _resolvedDevice);
            _recognizer = new OfflineRecognizer(config);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Не удалось загрузить GigaAM. Скачайте sherpa-совместимые модели через «Скачать GigaAM».", ex);
        }

        return Task.CompletedTask;
    }

    public Task<string> TranscribeAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
    {
        if (_recognizer is null)
        {
            throw new InvalidOperationException("GigaAM model is not loaded.");
        }

        using var stream = _recognizer.CreateStream();
        stream.AcceptWaveform(sampleRate, samples);
        _recognizer.Decode(stream);
        return Task.FromResult(stream.Result.Text.Trim());
    }

    public void Unload()
    {
        _recognizer?.Dispose();
        _recognizer = null;
        _usesE2e = false;
    }

    public void Dispose() => Unload();

    public static GigaAmBundlePaths? ResolveModelPaths(string modelDir) =>
        ModelRegistry.ResolveGigaAmBundle(modelDir);
}
