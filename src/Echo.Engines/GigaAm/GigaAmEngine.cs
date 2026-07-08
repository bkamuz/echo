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

    public GigaAmEngine(ILogger<GigaAmEngine> logger)
    {
        _logger = logger;
    }

    public string EngineId => "gigaam";
    public string DisplayName => $"GigaAM v3 ({_resolvedDevice.ToUpperInvariant()})";

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
            _logger.LogInformation("Loading GigaAM v3 (device={Device})", _resolvedDevice);
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
    }

    public void Dispose() => Unload();

    public static GigaAmModelPaths? ResolveModelPaths(string modelDir)
    {
        if (!Directory.Exists(modelDir))
        {
            return null;
        }

        // Sherpa-compatible layout (required for .NET)
        var sherpa = new GigaAmModelPaths(
            Path.Combine(modelDir, "gigaam_v3_rnnt_encoder.onnx"),
            Path.Combine(modelDir, "gigaam_v3_rnnt_decoder.onnx"),
            Path.Combine(modelDir, "gigaam_v3_rnnt_joint.onnx"),
            Path.Combine(modelDir, "gigaam_v3_rnnt_tokens.txt"));

        return File.Exists(sherpa.Encoder) ? sherpa : null;
    }

    public sealed record GigaAmModelPaths(string Encoder, string Decoder, string Joiner, string Tokens);
}
