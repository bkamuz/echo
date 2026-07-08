using echo.Abstractions.Core;
using echo.Abstractions.Engines;
using Microsoft.Extensions.Logging;
using SherpaOnnx;

namespace echo.Engines.Omnilingual;

public sealed class OmnilingualEngine : ITranscriptionEngine, IDisposable
{
    private readonly ILogger<OmnilingualEngine> _logger;
    private EngineOptions _config = new();
    private OfflineRecognizer? _recognizer;
    private string _resolvedDevice = "cpu";
    private string _loadedModelPath = string.Empty;
    private string _loadedDevice = string.Empty;

    public OmnilingualEngine(ILogger<OmnilingualEngine> logger)
    {
        _logger = logger;
    }

    public string EngineId => "omnilingual";
    public string DisplayName => $"Omnilingual ASR 300M ({_resolvedDevice.ToUpperInvariant()})";

    public void Configure(EngineOptions options) => _config = options;

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        var modelDir = AppPaths.OmnilingualDir;
        var modelPath = Path.Combine(modelDir, "model.int8.onnx");
        var tokensPath = Path.Combine(modelDir, "tokens.txt");
        var device = _config.Device == "cuda" ? "cuda" : "cpu";

        if (!File.Exists(modelPath) || !File.Exists(tokensPath))
        {
            throw new InvalidOperationException(
                "Omnilingual ASR модель не найдена. Скачайте модель через настройки приложения.");
        }

        if (_recognizer is not null && _loadedModelPath == modelPath && _loadedDevice == device)
        {
            return Task.CompletedTask;
        }

        Unload();

        _resolvedDevice = device;
        _loadedModelPath = modelPath;
        _loadedDevice = device;

        var config = new OfflineRecognizerConfig
        {
            FeatConfig = new FeatureConfig
            {
                SampleRate = _config.SampleRate,
                FeatureDim = 80,
            },
            ModelConfig = new OfflineModelConfig
            {
                Tokens = tokensPath,
                NumThreads = Math.Max(1, Environment.ProcessorCount),
                Provider = _resolvedDevice,
                Omnilingual = new OfflineOmnilingualAsrCtcModelConfig
                {
                    Model = modelPath,
                },
            },
        };

        try
        {
            _logger.LogInformation("Loading Omnilingual ASR 300M (device={Device})", _resolvedDevice);
            _recognizer = new OfflineRecognizer(config);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Не удалось загрузить Omnilingual ASR модель. Проверьте целостность файлов.", ex);
        }

        return Task.CompletedTask;
    }

    public Task<string> TranscribeAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
    {
        if (_recognizer is null)
        {
            throw new InvalidOperationException("Omnilingual model is not loaded.");
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
        _loadedModelPath = string.Empty;
        _loadedDevice = string.Empty;
    }

    public void Dispose() => Unload();
}
