using echo.Abstractions.Core;
using Microsoft.Extensions.Logging;
using SherpaOnnx;

namespace echo.Engines.Omnilingual;

public sealed class OmnilingualEngine : SherpaOfflineEngine
{
    public OmnilingualEngine(ILogger<OmnilingualEngine> logger)
        : base(logger)
    {
    }

    public override string EngineId => "omnilingual";

    public override string DisplayName =>
        $"Omnilingual ASR 300M ({ResolvedDevice.ToUpperInvariant()})";

    protected override string GetLoadKey()
    {
        var modelDir = AppPaths.OmnilingualDir;
        return Path.Combine(modelDir, "model.int8.onnx");
    }

    protected override string GetLoadFailedMessage() =>
        "Не удалось загрузить Omnilingual ASR модель. Проверьте целостность файлов.";

    protected override OfflineModelConfig CreateModelConfig(string provider)
    {
        var modelDir = AppPaths.OmnilingualDir;
        var modelPath = Path.Combine(modelDir, "model.int8.onnx");
        var tokensPath = Path.Combine(modelDir, "tokens.txt");

        if (!File.Exists(modelPath) || !File.Exists(tokensPath))
        {
            throw new InvalidOperationException(
                "Omnilingual ASR модель не найдена. Скачайте модель через настройки приложения.");
        }

        return new OfflineModelConfig
        {
            Tokens = tokensPath,
            NumThreads = Math.Max(1, Environment.ProcessorCount),
            Provider = provider,
            Omnilingual = new OfflineOmnilingualAsrCtcModelConfig
            {
                Model = modelPath,
            },
        };
    }
}
