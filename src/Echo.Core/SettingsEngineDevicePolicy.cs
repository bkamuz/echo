using echo.Abstractions.Engines;

namespace echo.Core;

/// <summary>
/// Keeps engine and compute-device settings consistent (NPU is Parakeet-only).
/// </summary>
public static class SettingsEngineDevicePolicy
{
    public static string NormalizeDeviceForEngine(string engine, string device)
    {
        if (engine != "parakeet_npu"
            && string.Equals(device, ExecutionProviderResolver.NpuDevice, StringComparison.Ordinal))
        {
            return ExecutionProviderResolver.CpuDevice;
        }

        return device;
    }
}
