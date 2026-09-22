using echo.Abstractions.Core;
using echo.Core;

namespace echo.Core.Tests;

public class EngineDisplayNamesTests
{
    [Theory]
    [InlineData("gigaam", "e2e", "cpu", "GigaAM v3 e2e (CPU)")]
    [InlineData("gigaam", "rnnt", "directml", "GigaAM v3 rnnt (DIRECTML)")]
    [InlineData("whisper", "base", "cpu", "Whisper base (CPU)")]
    [InlineData("omnilingual", "e2e", "cpu", "Omnilingual ASR 300M (CPU)")]
    [InlineData("parakeet_npu", "e2e", "npu", "Parakeet NPU (experimental)")]
    public void ForConfig_FormatsWithoutEngineResolution(
        string engine,
        string gigaAmSize,
        string device,
        string expected)
    {
        var config = new AppConfig
        {
            Engine = engine,
            GigaAmModelSize = gigaAmSize,
            WhisperModelSize = "base",
            Device = device,
        };

        Assert.Equal(expected, EngineDisplayNames.ForConfig(config));
    }
}
