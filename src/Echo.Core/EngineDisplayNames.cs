using echo.Abstractions.Core;
using echo.Abstractions.Engines;

namespace echo.Core;

/// <summary>
/// Formats engine labels from config without resolving native-bearing engine types.
/// </summary>
public static class EngineDisplayNames
{
    public static string ForConfig(AppConfig config)
    {
        var device = ExecutionProviderResolver
            .FromConfigDevice(config.Device)
            .ToString()
            .ToUpperInvariant();

        return ForEngine(config.Engine, config.WhisperModelSize, config.GigaAmModelSize, device);
    }

    public static string ForOptions(string engineId, EngineOptions options)
    {
        var device = ExecutionProviderResolver
            .FromConfigDevice(options.Device)
            .ToString()
            .ToUpperInvariant();

        return ForEngine(engineId, options.WhisperModelSize, options.GigaAmModelSize, device);
    }

    private static string ForEngine(
        string engineId,
        string whisperModelSize,
        string gigaAmModelSize,
        string device) =>
        engineId switch
        {
            "gigaam" => FormatGigaAm(gigaAmModelSize, device),
            "whisper" => $"Whisper {whisperModelSize} ({device})",
            "omnilingual" => $"Omnilingual ASR 300M ({device})",
            "parakeet_npu" => "Parakeet NPU (experimental)",
            _ => engineId,
        };

    private static string FormatGigaAm(string modelSize, string device) => modelSize switch
    {
        "rnnt" => $"GigaAM v3 rnnt ({device})",
        "e2e-ctc" => $"GigaAM v3 e2e-ctc ({device})",
        "multilingual" => $"GigaAM Multilingual CTC ({device})",
        "multilingual-large" => $"GigaAM Multilingual Large CTC ({device})",
        _ => $"GigaAM v3 e2e ({device})",
    };
}
