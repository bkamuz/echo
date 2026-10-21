using echo.Abstractions.Engines;
using echo.Core;

namespace echo.Core.Tests;

[Collection("AppConfigStatics")]
public sealed class RemovedEngineMigrationTests
{
    [Theory]
    [InlineData("nemotron", true, "parakeet_npu")]
    [InlineData("qwen3_asr", true, "parakeet_npu")]
    [InlineData("nemotron", false, "gigaam")]
    [InlineData("qwen3_asr", false, "gigaam")]
    public void Normalize_MigratesRemovedEngine(string saved, bool parakeet, string expected)
    {
        AppConfig.ParakeetAvailableOverride = parakeet;
        try
        {
            var c = new AppConfig { Engine = saved, Device = ExecutionProviderResolver.NpuDevice };
            c.Normalize();
            Assert.Equal(expected, c.Engine);
            Assert.Equal(ExecutionProviderResolver.CpuDevice, c.Device);
        }
        finally
        {
            AppConfig.ParakeetAvailableOverride = null;
        }
    }

    [Fact]
    public void RemovedEngines_AreGoneFromRegistry()
    {
        Assert.DoesNotContain("nemotron", AppConfig.Engines);
        Assert.DoesNotContain("qwen3_asr", AppConfig.Engines);
        Assert.Null(echo.Abstractions.Core.ModelRegistry.SpecForEngine("nemotron", "small", "e2e"));
    }

    [Fact]
    public void ConfigStore_LoadsLegacyQwenConfigWithoutCrash()
    {
        var c = System.Text.Json.JsonSerializer.Deserialize<AppConfig>("{\"engine\":\"qwen3_asr\",\"device\":\"cpu\"}")!;
        c.Normalize();
        Assert.Contains(c.Engine, AppConfig.Engines);
    }
}
