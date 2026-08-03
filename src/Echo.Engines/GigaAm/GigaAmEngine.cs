using System.Text.RegularExpressions;
using echo.Abstractions.Core;
using echo.Abstractions.Engines;
using Microsoft.Extensions.Logging;
using SherpaOnnx;

namespace echo.Engines.GigaAm;

public sealed class GigaAmEngine : SherpaOfflineEngine
{
    private string _variant = "e2e";

    public GigaAmEngine(ILogger<GigaAmEngine> logger)
        : base(logger)
    {
    }

    public override string EngineId => "gigaam";

    public override string DisplayName
    {
        get
        {
            var device = ResolvedDevice.ToUpperInvariant();
            return Config.GigaAmModelSize switch
            {
                "rnnt" => $"GigaAM v3 rnnt ({device})",
                "e2e-ctc" => $"GigaAM v3 e2e-ctc ({device})",
                _ => $"GigaAM v3 e2e ({device})",
            };
        }
    }

    protected override string GetLoadKey() => Config.GigaAmModelSize;

    protected override string GetLoadFailedMessage() =>
        "Не удалось загрузить GigaAM. Проверьте целостность модели.";

    protected override OfflineModelConfig CreateModelConfig(string provider)
    {
        _variant = Config.GigaAmModelSize;
        if (ModelRegistry.IsGigaAmCtcVariant(_variant))
        {
            return CreateCtcModelConfig(provider);
        }

        return CreateTransducerModelConfig(provider);
    }

    private OfflineModelConfig CreateTransducerModelConfig(string provider)
    {
        var bundle = ModelRegistry.ResolveGigaAmBundle(AppPaths.GigaAmDir, _variant);
        if (bundle is null)
        {
            throw new InvalidOperationException(
                $"GigaAM {_variant} модель не найдена. Скачайте через настройки приложения.");
        }

        return new OfflineModelConfig
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
        };
    }

    private OfflineModelConfig CreateCtcModelConfig(string provider)
    {
        var ctc = ModelRegistry.ResolveGigaAmCtc(AppPaths.GigaAmDir, _variant);
        if (ctc is null)
        {
            throw new InvalidOperationException(
                $"GigaAM {_variant} модель не найдена. Скачайте через настройки приложения.");
        }

        return new OfflineModelConfig
        {
            Tokens = ctc.Tokens,
            NumThreads = Math.Max(1, Environment.ProcessorCount),
            Provider = provider,
            NeMoCtc = new OfflineNemoEncDecCtcModelConfig
            {
                Model = ctc.Model,
            },
        };
    }

    protected override string PostProcess(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        foreach (var (ru, en) in Replacements)
        {
            text = Regex.Replace(text, $@"\b{ru}\b", en, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        return text;
    }

    private static readonly (string Ru, string En)[] Replacements =
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
}
