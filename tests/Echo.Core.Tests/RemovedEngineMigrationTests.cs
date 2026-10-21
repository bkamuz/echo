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

    [Fact]
    public void Normalize_WhisperNpu_KeptOnWindows_MigratedToCpuTurboElsewhere()
    {
        try
        {
            AppConfig.ParakeetAvailableOverride = true;
            var win = new AppConfig { Engine = "whisper_npu", Device = ExecutionProviderResolver.NpuDevice };
            win.Normalize();
            Assert.Equal("whisper_npu", win.Engine);
            Assert.Equal(ExecutionProviderResolver.NpuDevice, win.Device);

            AppConfig.ParakeetAvailableOverride = false;
            var linux = new AppConfig { Engine = "whisper_npu", Device = ExecutionProviderResolver.NpuDevice };
            linux.Normalize();
            Assert.Equal("whisper", linux.Engine);
            Assert.Equal("large-v3-turbo", linux.WhisperModelSize);
            Assert.Equal(ExecutionProviderResolver.CpuDevice, linux.Device);
        }
        finally
        {
            AppConfig.ParakeetAvailableOverride = null;
        }
    }
}
