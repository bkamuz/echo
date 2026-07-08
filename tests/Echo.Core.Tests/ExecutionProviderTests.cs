using echo.Abstractions.Engines;
using echo.Core;

namespace echo.Core.Tests;

public class ExecutionProviderTests
{
    [Theory]
    [InlineData(ExecutionProvider.Cpu, "cpu")]
    [InlineData(ExecutionProvider.DirectMl, "directml")]
    public void ToSherpaProvider_MapsKnownValues(ExecutionProvider provider, string expected)
    {
        Assert.Equal(expected, ExecutionProviderResolver.ToSherpaProvider(provider));
    }

    [Theory]
    [InlineData("cpu", ExecutionProvider.Cpu)]
    [InlineData("directml", ExecutionProvider.DirectMl)]
    [InlineData("cuda", ExecutionProvider.Cpu)]
    [InlineData("unknown", ExecutionProvider.Cpu)]
    [InlineData(null, ExecutionProvider.Cpu)]
    public void FromConfigDevice_ParsesAndMigratesLegacy(string? device, ExecutionProvider expected)
    {
        Assert.Equal(expected, ExecutionProviderResolver.FromConfigDevice(device));
    }

    [Fact]
    public void AppConfig_Normalize_MigratesCudaToCpu()
    {
        var config = new AppConfig { Device = "cuda" };
        config.Normalize();
        Assert.Equal(ExecutionProviderResolver.CpuDevice, config.Device);
    }

    [Fact]
    public void AppConfig_Normalize_KeepsDirectMl()
    {
        var config = new AppConfig { Device = ExecutionProviderResolver.DirectMlDevice };
        config.Normalize();
        Assert.Equal(ExecutionProviderResolver.DirectMlDevice, config.Device);
    }

    [Fact]
    public void AppConfig_Devices_ListsCpuAndDirectMlOnly()
    {
        Assert.Equal(
            [ExecutionProviderResolver.CpuDevice, ExecutionProviderResolver.DirectMlDevice],
            AppConfig.Devices);
    }
}
