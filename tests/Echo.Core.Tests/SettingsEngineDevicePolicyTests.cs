using echo.Abstractions.Engines;
using echo.Core;

namespace echo.Core.Tests;

public sealed class SettingsEngineDevicePolicyTests
{
    [Theory]
    [InlineData("gigaam", ExecutionProviderResolver.NpuDevice, ExecutionProviderResolver.CpuDevice)]
    [InlineData("whisper", ExecutionProviderResolver.NpuDevice, ExecutionProviderResolver.CpuDevice)]
    [InlineData("omnilingual", ExecutionProviderResolver.NpuDevice, ExecutionProviderResolver.CpuDevice)]
    [InlineData("parakeet_npu", ExecutionProviderResolver.NpuDevice, ExecutionProviderResolver.NpuDevice)]
    [InlineData("gigaam", ExecutionProviderResolver.CpuDevice, ExecutionProviderResolver.CpuDevice)]
    public void NormalizeDeviceForEngine_KeepsNpuOnlyForParakeet(string engine, string device, string expected)
    {
        Assert.Equal(expected, SettingsEngineDevicePolicy.NormalizeDeviceForEngine(engine, device));
    }

    [Fact]
    public void AppConfig_Normalize_DemotesNpuWhenEngineIsNotParakeet()
    {
        var config = new AppConfig
        {
            Engine = "gigaam",
            Device = ExecutionProviderResolver.NpuDevice,
        };
        config.Normalize();
        Assert.Equal(ExecutionProviderResolver.CpuDevice, config.Device);
        Assert.Equal("gigaam", config.Engine);
    }
}
