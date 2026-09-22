namespace echo.Abstractions.Platform;

/// <summary>
/// Probes Qualcomm NPU (QNN / HTP) availability on Windows on Snapdragon (win-arm64).
/// </summary>
public interface INpuAvailability
{
    /// <summary>True on native Windows ARM64 where an NPU option may be offered.</summary>
    bool IsLikelyPlatform { get; }

    /// <summary>True when Snapdragon / QNN runtime indicators are present on this machine.</summary>
    bool IsHardwareDetected { get; }

    /// <summary>True when Echo may offer NPU in settings (platform + hardware heuristics).</summary>
    bool IsAvailable { get; }

    /// <summary>Human-readable probe summary for logs and disabled tooltips.</summary>
    string StatusDetail { get; }
}
