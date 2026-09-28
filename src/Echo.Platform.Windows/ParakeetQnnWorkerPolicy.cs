namespace echo.Platform.Windows;

/// <summary>
/// Parakeet NPU (QNN/HTP) runs in an isolated child process on Windows ARM64 so native
/// aborts during EP registration or session creation cannot kill the Echo UI.
/// </summary>
public static class ParakeetQnnWorkerPolicy
{
    public static bool ShouldIsolateNpu() =>
        OperatingSystem.IsWindows()
        && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
            is System.Runtime.InteropServices.Architecture.Arm64
            or System.Runtime.InteropServices.Architecture.Arm;

    public static bool IsNpuDevice(string? configDevice) =>
        string.Equals(configDevice, "npu", StringComparison.OrdinalIgnoreCase);
}
