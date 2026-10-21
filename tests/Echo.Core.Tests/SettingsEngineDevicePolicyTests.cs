using echo.Abstractions.Engines;
using echo.Core;

namespace echo.Core.Tests;

public sealed class SettingsEngineDevicePolicyTests
{
    private static readonly string[] UiEngineOrder =
        ["gigaam", "whisper", "omnilingual", "parakeet_npu"];

    [Theory]
    [InlineData("gigaam", ExecutionProviderResolver.NpuDevice, false)]
    [InlineData("whisper", ExecutionProviderResolver.NpuDevice, false)]
    [InlineData("omnilingual", ExecutionProviderResolver.NpuDevice, false)]
    [InlineData("parakeet_npu", ExecutionProviderResolver.NpuDevice, true)]
    [InlineData("gigaam", ExecutionProviderResolver.CpuDevice, true)]
    [InlineData("gigaam", ExecutionProviderResolver.DirectMlDevice, true)]
    [InlineData("parakeet_npu", ExecutionProviderResolver.CpuDevice, true)]
    [InlineData("whisper", ExecutionProviderResolver.DirectMlDevice, false)]
    public void SupportsDevice_MatchesCapabilityTable(string engine, string device, bool expected)
    {
        Assert.Equal(expected, SettingsEngineDevicePolicy.SupportsDevice(engine, device));
    }

    [Fact]
    public void FilterEnginesForDevice_Npu_ShowsOnlyParakeet()
    {
        var filtered = SettingsEngineDevicePolicy.FilterEnginesForDevice(
            UiEngineOrder,
            ExecutionProviderResolver.NpuDevice);
        Assert.Equal(["parakeet_npu"], filtered);
    }

    [Fact]
    public void FilterEnginesForDevice_Cpu_IncludesAllRegisteredEngines()
    {
        var filtered = SettingsEngineDevicePolicy.FilterEnginesForDevice(
            UiEngineOrder,
            ExecutionProviderResolver.CpuDevice);
        Assert.Equal(UiEngineOrder, filtered);
    }

    [Fact]
    public void FilterEnginesForDevice_DirectMl_ExcludesWhisperAndParakeet()
    {
        var filtered = SettingsEngineDevicePolicy.FilterEnginesForDevice(
            UiEngineOrder,
            ExecutionProviderResolver.DirectMlDevice);
        Assert.Equal(["gigaam", "omnilingual"], filtered);
    }

    [Theory]
    [InlineData("gigaam", ExecutionProviderResolver.NpuDevice, "parakeet_npu")]
    [InlineData("parakeet_npu", ExecutionProviderResolver.CpuDevice, "parakeet_npu")]
    [InlineData("parakeet_npu", ExecutionProviderResolver.NpuDevice, "parakeet_npu")]
    [InlineData("whisper", ExecutionProviderResolver.DirectMlDevice, "gigaam")]
    public void ResolveEngineForDevice_KeepsEngineWhenValidElseFirst(
        string currentEngine,
        string device,
        string expected)
    {
        var resolved = SettingsEngineDevicePolicy.ResolveEngineForDevice(
            currentEngine,
            device,
            UiEngineOrder);
        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData("gigaam", ExecutionProviderResolver.NpuDevice, ExecutionProviderResolver.CpuDevice)]
    [InlineData("whisper", ExecutionProviderResolver.NpuDevice, ExecutionProviderResolver.CpuDevice)]
    [InlineData("omnilingual", ExecutionProviderResolver.NpuDevice, ExecutionProviderResolver.CpuDevice)]
    [InlineData("parakeet_npu", ExecutionProviderResolver.NpuDevice, ExecutionProviderResolver.NpuDevice)]
    [InlineData("gigaam", ExecutionProviderResolver.CpuDevice, ExecutionProviderResolver.CpuDevice)]
    [InlineData("parakeet_npu", ExecutionProviderResolver.DirectMlDevice, ExecutionProviderResolver.CpuDevice)]
    public void NormalizeDeviceForEngine_PicksSupportedDevice(string engine, string device, string expected)
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
