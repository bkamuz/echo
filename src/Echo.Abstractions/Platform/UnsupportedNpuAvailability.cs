namespace echo.Abstractions.Platform;

/// <summary>
/// Shared stub for platforms without Qualcomm NPU (Linux / macOS / Windows x64).
/// </summary>
public sealed class UnsupportedNpuAvailability : INpuAvailability
{
    public bool IsLikelyPlatform => false;

    public bool IsHardwareDetected => false;

    public bool IsAvailable => false;

    public string StatusDetail => "Qualcomm NPU is only supported on Windows ARM64 (Snapdragon).";
}
