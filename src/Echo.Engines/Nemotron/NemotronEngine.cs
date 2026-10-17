using echo.Abstractions.Core;
using echo.Abstractions.Engines;
using Microsoft.Extensions.Logging;
using SherpaOnnx;

namespace echo.Engines.Nemotron;

/// <summary>
/// NVIDIA Nemotron 3.5 ASR Streaming 0.6B (cache-aware FastConformer RNNT, 560 ms chunks)
/// via sherpa-onnx <see cref="OnlineRecognizer"/>. Echo records a whole utterance, so the
/// recorded buffer is streamed through the recognizer and flushed with InputFinished.
/// CPU only for now (see SettingsEngineDevicePolicy).
/// </summary>
public sealed class NemotronEngine : ITranscriptionEngine, IDisposable
{
    private readonly ILogger<NemotronEngine> _logger;
    private OnlineRecognizer? _recognizer;
    private readonly string? _modelDirOverride;
    private EngineOptions _config = new();

    public NemotronEngine(ILogger<NemotronEngine> logger)
        : this(logger, null)
    {
    }

    /// <summary>Test hook: load the model from an explicit directory.</summary>
    public NemotronEngine(ILogger<NemotronEngine> logger, string? modelDir)
    {
        _logger = logger;
        _modelDirOverride = modelDir;
    }

    public string EngineId => "nemotron";

    public string DisplayName => "Nemotron 3.5 Streaming 0.6B (CPU)";

    public void Configure(EngineOptions options) => _config = options;

    public static OnlineRecognizerConfig BuildConfig(string modelDir, int sampleRate, int numThreads)
    {
        var config = new OnlineRecognizerConfig();
        config.FeatConfig.SampleRate = sampleRate;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Transducer.Encoder = Path.Combine(modelDir, "encoder.int8.onnx");
        config.ModelConfig.Transducer.Decoder = Path.Combine(modelDir, "decoder.int8.onnx");
        config.ModelConfig.Transducer.Joiner = Path.Combine(modelDir, "joiner.int8.onnx");
        config.ModelConfig.Tokens = Path.Combine(modelDir, "tokens.txt");
        config.ModelConfig.NumThreads = Math.Max(1, numThreads);
        config.ModelConfig.Provider = "cpu";
        config.DecodingMethod = "greedy_search";
        config.EnableEndpoint = 0;
        return config;
    }

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_recognizer is not null)
        {
            return Task.CompletedTask;
        }

        var dir = _modelDirOverride ?? AppPaths.NemotronDir;
        if (!ModelRegistry.IsNemotronDownloaded(dir))
        {
            throw new InvalidOperationException(
                "Модель Nemotron 3.5 не найдена. Скачайте модель через настройки приложения.");
        }

        var config = BuildConfig(dir, _config.SampleRate, Math.Min(8, Environment.ProcessorCount));
        try
        {
            _logger.LogInformation("Loading nemotron (provider=cpu, threads={Threads})", config.ModelConfig.NumThreads);
            _recognizer = new OnlineRecognizer(config);
            _logger.LogInformation("Loaded nemotron");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load nemotron");
            throw new InvalidOperationException(
                "Не удалось загрузить модель Nemotron 3.5. Проверьте целостность файлов.", ex);
        }

        return Task.CompletedTask;
    }

    public Task<string> TranscribeAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
    {
        var recognizer = _recognizer ?? throw new InvalidOperationException("nemotron model is not loaded.");
        using var stream = recognizer.CreateStream();
        stream.SetOption("language", ModelRegistry.NemotronLanguageFor(_config.Language));
        stream.AcceptWaveform(sampleRate, samples);
        // Tail padding so the last 560 ms chunk is emitted before InputFinished.
        stream.AcceptWaveform(sampleRate, new float[sampleRate]);
        stream.InputFinished();
        while (recognizer.IsReady(stream))
        {
            cancellationToken.ThrowIfCancellationRequested();
            recognizer.Decode(stream);
        }

        var text = CollapseWhitespace(recognizer.GetResult(stream).Text);
        return Task.FromResult(text);
    }

    /// <summary>Nemotron emits a double space between internal segments.</summary>
    public static string CollapseWhitespace(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    public void Unload()
    {
        _recognizer?.Dispose();
        _recognizer = null;
    }

    public void Dispose() => Unload();
}
