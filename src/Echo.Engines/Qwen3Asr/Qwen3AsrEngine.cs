using echo.Abstractions.Core;
using echo.Abstractions.Engines;
using Microsoft.Extensions.Logging;
using SherpaOnnx;

namespace echo.Engines.Qwen3Asr;

/// <summary>
/// Qwen3-ASR 0.6B int8 (LLM decoder, 30 languages) via sherpa-onnx <see cref="OfflineRecognizer"/>.
/// CPU only for now (see SettingsEngineDevicePolicy). Silent/empty clips short-circuit to an empty
/// result so the LLM decoder cannot hallucinate text on silence.
/// </summary>
public sealed class Qwen3AsrEngine : ITranscriptionEngine, IDisposable
{
    /// <summary>Peak amplitude below which a clip is treated as silence.</summary>
    public const float SilencePeakThreshold = 0.01f;

    private readonly ILogger<Qwen3AsrEngine> _logger;
    private readonly string? _modelDirOverride;
    private OfflineRecognizer? _recognizer;
    private EngineOptions _config = new();

    public Qwen3AsrEngine(ILogger<Qwen3AsrEngine> logger)
        : this(logger, null)
    {
    }

    /// <summary>Test hook: load the model from an explicit directory.</summary>
    public Qwen3AsrEngine(ILogger<Qwen3AsrEngine> logger, string? modelDir)
    {
        _logger = logger;
        _modelDirOverride = modelDir;
    }

    public string EngineId => "qwen3_asr";

    public string DisplayName => "Qwen3-ASR 0.6B (CPU)";

    public void Configure(EngineOptions options) => _config = options;

    public static OfflineRecognizerConfig BuildConfig(string modelDir, int sampleRate, int numThreads)
    {
        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = sampleRate;
        config.FeatConfig.FeatureDim = 128;
        var model = new OfflineModelConfig
        {
            NumThreads = Math.Max(1, numThreads),
            Provider = "cpu",
        };
        var qwen = new OfflineQwen3AsrModelConfig
        {
            ConvFrontend = Path.Combine(modelDir, "conv_frontend.onnx"),
            Encoder = Path.Combine(modelDir, "encoder.int8.onnx"),
            Decoder = Path.Combine(modelDir, "decoder.int8.onnx"),
            Tokenizer = Path.Combine(modelDir, "tokenizer"),
            // Dictation can run past the 128-token default; audio tokens also count toward total.
            MaxTotalLen = 2048,
            MaxNewTokens = 768,
        };
        model.Qwen3Asr = qwen;
        config.ModelConfig = model;
        return config;
    }

    public static bool IsSilent(float[] samples)
    {
        if (samples.Length == 0)
        {
            return true;
        }

        float peak = 0;
        foreach (var s in samples)
        {
            var a = Math.Abs(s);
            if (a > peak)
            {
                peak = a;
            }
        }

        return peak < SilencePeakThreshold;
    }

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_recognizer is not null)
        {
            return Task.CompletedTask;
        }

        var dir = _modelDirOverride ?? AppPaths.Qwen3AsrDir;
        if (!ModelRegistry.IsQwen3AsrDownloaded(dir))
        {
            throw new InvalidOperationException(
                "Модель Qwen3-ASR не найдена. Скачайте модель через настройки приложения.");
        }

        var config = BuildConfig(dir, _config.SampleRate, Math.Min(8, Environment.ProcessorCount));
        try
        {
            _logger.LogInformation("Loading qwen3_asr (provider=cpu, threads={Threads})", config.ModelConfig.NumThreads);
            _recognizer = new OfflineRecognizer(config);
            _logger.LogInformation("Loaded qwen3_asr");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load qwen3_asr");
            throw new InvalidOperationException(
                "Не удалось загрузить модель Qwen3-ASR. Проверьте целостность файлов.", ex);
        }

        return Task.CompletedTask;
    }

    public Task<string> TranscribeAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
    {
        var recognizer = _recognizer ?? throw new InvalidOperationException("qwen3_asr model is not loaded.");
        if (IsSilent(samples))
        {
            return Task.FromResult(string.Empty);
        }

        using var stream = recognizer.CreateStream();
        var language = ModelRegistry.Qwen3AsrLanguageFor(_config.Language);
        if (language.Length > 0)
        {
            stream.SetOption("language", language);
        }

        stream.AcceptWaveform(sampleRate, samples);
        recognizer.Decode(stream);
        var text = System.Text.RegularExpressions.Regex.Replace(stream.Result.Text, @"\s+", " ").Trim();
        return Task.FromResult(text);
    }

    public void Unload()
    {
        _recognizer?.Dispose();
        _recognizer = null;
    }

    public void Dispose() => Unload();
}
