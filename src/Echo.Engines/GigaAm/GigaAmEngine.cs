using System.Text.RegularExpressions;
using echo.Abstractions.Core;
using echo.Abstractions.Engines;
using Microsoft.Extensions.Logging;
using SherpaOnnx;

namespace echo.Engines.GigaAm;

public sealed class GigaAmEngine : SherpaOfflineEngine
{
    private string _variant = "e2e";
    private GigaAmBundlePaths? _bundle;

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
        _bundle = ModelRegistry.ResolveGigaAmBundle(AppPaths.GigaAmDir, _variant);
        if (_bundle is null)
        {
            throw new InvalidOperationException(
                $"GigaAM {_variant} модель не найдена. Скачайте через настройки приложения.");
        }

        return new OfflineModelConfig
        {
            Tokens = _bundle.Tokens,
            NumThreads = Math.Max(1, Environment.ProcessorCount),
            Provider = provider,
            Transducer = new OfflineTransducerModelConfig
            {
                Encoder = _bundle.Encoder,
                Decoder = _bundle.Decoder,
                Joiner = _bundle.Joiner,
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
