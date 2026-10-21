using echo.Abstractions.Core;
using Microsoft.Extensions.Logging;
using SherpaOnnx;

namespace echo.Engines.Whisper;

/// <summary>
/// OpenAI Whisper via sherpa-onnx (int8 ONNX, downloaded on demand). Replaces Whisper.net so
/// Release builds ship no extra natives — sherpa-onnx is already bundled for GigaAM.
/// </summary>
public sealed class WhisperEngine : SherpaOfflineEngine
{
    public WhisperEngine(ILogger<WhisperEngine> logger)
        : base(logger)
    {
    }

    public override string EngineId => "whisper";

    public override string DisplayName =>
        $"Whisper {Config.WhisperModelSize} ({ResolvedDevice.ToUpperInvariant()})";

    protected override int FeatureDim => Config.WhisperModelSize is "large-v3" or "large-v3-turbo" ? 128 : 80;

    // Whisper decoder graphs are not reliable under DirectML; keep CPU (policy is CPU-only anyway).
    protected override string PreferProvider(string requestedProvider) => "cpu";

    protected override string GetLoadKey() =>
        $"{ModelRegistry.WhisperFiles(Config.WhisperModelSize).Encoder}|{Config.Language}";

    protected override string GetLoadFailedMessage() =>
        "Не удалось загрузить модель Whisper. Проверьте целостность файлов.";

    protected override OfflineModelConfig CreateModelConfig(string provider)
    {
        var files = ModelRegistry.WhisperFiles(Config.WhisperModelSize);
        if (!ModelRegistry.IsWhisperDownloaded(Config.WhisperModelSize))
        {
            throw new InvalidOperationException(
                "Модель Whisper не найдена. Скачайте модель через настройки приложения.");
        }

        var model = new OfflineModelConfig
        {
            Tokens = files.Tokens,
            NumThreads = Math.Min(8, Math.Max(1, Environment.ProcessorCount)),
            Provider = provider,
        };
        var whisper = new OfflineWhisperModelConfig
        {
            Encoder = files.Encoder,
            Decoder = files.Decoder,
            Language = ModelRegistry.WhisperLanguageFor(Config.Language),
            Task = "transcribe",
            TailPaddings = -1,
        };
        model.Whisper = whisper;
        return model;
    }
}
