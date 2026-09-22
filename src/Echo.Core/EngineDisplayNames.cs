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

        return config.Engine switch
        {
            "gigaam" => FormatGigaAm(config.GigaAmModelSize, device),
            "whisper" => $"Whisper {config.WhisperModelSize} ({device})",
            "omnilingual" => $"Omnilingual ASR 300M ({device})",
            "parakeet_npu" => "Parakeet NPU (experimental)",
            _ => config.Engine,
        };
    }

    private static string FormatGigaAm(string modelSize, string device) => modelSize switch
    {
        "rnnt" => $"GigaAM v3 rnnt ({device})",
        "e2e-ctc" => $"GigaAM v3 e2e-ctc ({device})",
        "multilingual" => $"GigaAM Multilingual CTC ({device})",
        "multilingual-large" => $"GigaAM Multilingual Large CTC ({device})",
        _ => $"GigaAM v3 e2e ({device})",
    };
}
