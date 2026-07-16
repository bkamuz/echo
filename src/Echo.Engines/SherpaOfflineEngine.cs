using echo.Abstractions.Engines;
using Microsoft.Extensions.Logging;
using SherpaOnnx;

namespace echo.Engines;

public abstract class SherpaOfflineEngine : ITranscriptionEngine, IDisposable
{
    private readonly ILogger _logger;
    private OfflineRecognizer? _recognizer;
    private string _resolvedDevice = "cpu";
    private string _loadedKey = string.Empty;
    private string _loadedDevice = string.Empty;

    protected SherpaOfflineEngine(ILogger logger)
    {
        _logger = logger;
    }

    protected EngineOptions Config { get; private set; } = new();

    protected string ResolvedDevice => _resolvedDevice;

    public abstract string EngineId { get; }

    public abstract string DisplayName { get; }

    public void Configure(EngineOptions options) => Config = options;

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        var loadKey = GetLoadKey();
        var requestedProvider = SherpaProviderHelper.ResolveSherpaProvider(Config.Device);

        if (_recognizer is not null
            && _loadedKey == loadKey
            && _loadedDevice == requestedProvider)
        {
            return Task.CompletedTask;
        }

        Unload();
        _loadedKey = loadKey;

        // Resolve model paths first — missing files should surface as InvalidOperationException.
        _ = CreateModelConfig(requestedProvider);

        if (!TryCreateWithFallback(requestedProvider, out var recognizer, out var resolved))
        {
            throw new InvalidOperationException(GetLoadFailedMessage());
        }

        _resolvedDevice = resolved;
        _loadedDevice = resolved;
        _recognizer = recognizer;
        return Task.CompletedTask;
    }

    public Task<string> TranscribeAsync(
        float[] samples,
        int sampleRate,
        CancellationToken cancellationToken = default)
    {
        if (_recognizer is null)
        {
            throw new InvalidOperationException($"{EngineId} model is not loaded.");
        }

        using var stream = _recognizer.CreateStream();
        stream.AcceptWaveform(sampleRate, samples);
        _recognizer.Decode(stream);
        var text = stream.Result.Text.Trim();
        return Task.FromResult(PostProcess(text));
    }

    public void Unload()
    {
        _recognizer?.Dispose();
        _recognizer = null;
        _loadedKey = string.Empty;
        _loadedDevice = string.Empty;
    }

    public void Dispose() => Unload();

    protected abstract string GetLoadKey();

    protected abstract string GetLoadFailedMessage();

    protected abstract OfflineModelConfig CreateModelConfig(string provider);

    protected virtual string PostProcess(string text) => text;

    protected virtual int FeatureDim => 80;

    private bool TryCreateWithFallback(
        string requestedProvider,
        out OfflineRecognizer? recognizer,
        out string resolvedProvider)
    {
        recognizer = null;
        resolvedProvider = requestedProvider;

        if (TryCreateRecognizer(requestedProvider, out recognizer))
        {
            return true;
        }

        if (requestedProvider == "directml")
        {
            _logger.LogWarning("DirectML load failed for {Engine}; falling back to CPU", EngineId);
            if (TryCreateRecognizer("cpu", out recognizer))
            {
                resolvedProvider = "cpu";
                return true;
            }
        }
        else if (requestedProvider == "cpu")
        {
            _logger.LogWarning("CPU provider failed for {Engine}; falling back to DirectML", EngineId);
            if (TryCreateRecognizer("directml", out recognizer))
            {
                resolvedProvider = "directml";
                return true;
            }
        }

        return false;
    }

    private bool TryCreateRecognizer(string provider, out OfflineRecognizer? recognizer)
    {
        recognizer = null;
        OfflineRecognizerConfig config;
        try
        {
            config = new OfflineRecognizerConfig
            {
                FeatConfig = new FeatureConfig
                {
                    SampleRate = Config.SampleRate,
                    FeatureDim = FeatureDim,
                },
                ModelConfig = CreateModelConfig(provider),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build model config for {Engine}", EngineId);
            return false;
        }

        try
        {
            _logger.LogInformation("Loading {Engine} (provider={Provider})", EngineId, provider);
            recognizer = new OfflineRecognizer(config);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load {Engine} with provider {Provider}", EngineId, provider);
            return false;
        }
    }
}
