using System.Text.RegularExpressions;
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
    private string _loadedVariant = string.Empty;

    public GigaAmEngine(ILogger<GigaAmEngine> logger)
    {
        _logger = logger;
    }

    public string EngineId => "gigaam";
    public string DisplayName
    {
        get
        {
            var device = _resolvedDevice.ToUpperInvariant();
            return _config.GigaAmModelSize switch
            {
                "rnnt" => $"GigaAM v3 rnnt ({device})",
                _ => $"GigaAM v3 e2e ({device})",
            };
        }
    }

    public void Configure(EngineOptions options) => _config = options;

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        var variant = _config.GigaAmModelSize;
        var requestedProvider = SherpaProviderHelper.ResolveSherpaProvider(_config.Device);
        if (_recognizer is not null && _loadedVariant == variant && _resolvedDevice == requestedProvider)
        {
            return Task.CompletedTask;
        }

        Unload();
        var bundle = ModelRegistry.ResolveGigaAmBundle(AppPaths.GigaAmDir, variant);
        _loadedVariant = variant;

        if (bundle is null)
        {
            throw new InvalidOperationException(
                $"GigaAM {variant} модель не найдена. Скачайте через настройки приложения.");
        }

        if (TryCreateRecognizer(bundle, requestedProvider, out var recognizer))
        {
            _resolvedDevice = requestedProvider;
            _recognizer = recognizer;
            return Task.CompletedTask;
        }

        if (requestedProvider == "directml")
        {
            _logger.LogWarning("DirectML load failed for GigaAM; falling back to CPU");
            if (TryCreateRecognizer(bundle, "cpu", out recognizer))
            {
                _resolvedDevice = "cpu";
                _recognizer = recognizer;
                return Task.CompletedTask;
            }
        }

        throw new InvalidOperationException(
            "Не удалось загрузить GigaAM. Проверьте целостность модели.");
    }

    private bool TryCreateRecognizer(GigaAmBundlePaths bundle, string provider, out OfflineRecognizer? recognizer)
    {
        recognizer = null;
        var config = new OfflineRecognizerConfig
        {
            FeatConfig = new FeatureConfig
            {
                SampleRate = _config.SampleRate,
                FeatureDim = 80,
            },
            ModelConfig = new OfflineModelConfig
            {
                Tokens = bundle.Tokens,
                NumThreads = Math.Max(1, Environment.ProcessorCount),
                Provider = provider,
                Transducer = new OfflineTransducerModelConfig
                {
                    Encoder = bundle.Encoder,
                    Decoder = bundle.Decoder,
                    Joiner = bundle.Joiner,
                },
            },
        };

        try
        {
            _logger.LogInformation(
                "Loading GigaAM {Variant} (provider={Provider})",
                _loadedVariant,
                provider);
            recognizer = new OfflineRecognizer(config);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load GigaAM with provider {Provider}", provider);
            return false;
        }
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
        var text = stream.Result.Text.Trim();
        return Task.FromResult(PostProcess(text));
    }

    private static string PostProcess(string text)
    {
        // Словарь: русское звучание -> английское слово.
        // GigaAM — чисто русская модель, английские токены в ней отсутствуют.
        // Этот пост-процессинг исправляет часто используемые английские термины.
        if (string.IsNullOrEmpty(text)) return text;

        foreach (var (ru, en) in _replacements)
        {
            text = Regex.Replace(text, $@"\b{ru}\b", en, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
        return text;
    }

    private static readonly (string Ru, string En)[] _replacements =
    [
        ("девайс", "device"),
        ("билд", "build"),
        ("интерфейс", "interface"),
        ("апдейт", "update"),
        ("релиз", "release"),
        ("деплой", "deploy"),
        ("сервер", "server"),
        ("клиент", "client"),
        ("конфиг", "config"),
        ("баг", "bug"),
        ("фикс", "fix"),
        ("фича", "feature"),
        ("коммит", "commit"),
        ("пулл", "pull"),
        ("бранч", "branch"),
        ("репозиторий", "repository"),
        ("лонг", "long"),
        ("шорт", "short"),
        ("инт", "int"),
        ("стринг", "string"),
        ("бул", "bool"),
        ("класс", "class"),
        ("метод", "method"),
        ("функция", "function"),
        ("переменная", "variable"),
        ("массив", "array"),
        ("список", "list"),
        ("словарь", "dictionary"),
    ];

    public void Unload()
    {
        _recognizer?.Dispose();
        _recognizer = null;
    }

    public void Dispose() => Unload();
}
