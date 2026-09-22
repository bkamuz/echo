using System.Runtime.Versioning;
using Microsoft.Win32;

namespace echo.Engines.ParakeetNpu;

/// <summary>
/// Chipset gating for Hexagon V73 HTP binaries (Snapdragon X Elite).
/// </summary>
[SupportedOSPlatform("windows")]
public static class SnapdragonHardware
{
    public static bool IsWindowsArm64 =>
        OperatingSystem.IsWindows()
        && (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
            is System.Runtime.InteropServices.Architecture.Arm64
            or System.Runtime.InteropServices.Architecture.Arm);

    public static bool IsSnapdragonXElite(out string processorName)
    {
        processorName = TryReadProcessorName() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(processorName))
        {
            return false;
        }

        var normalized = processorName.ToLowerInvariant();
        return normalized.Contains("snapdragon")
            && (normalized.Contains("x elite") || normalized.Contains("x1e"));
    }

    public static void EnsureSnapdragonXElite()
    {
        if (!IsWindowsArm64)
        {
            throw new PlatformNotSupportedException(
                "Parakeet NPU requires Windows on ARM64 (Snapdragon Copilot+ PC).");
        }

        if (!IsSnapdragonXElite(out var processor))
        {
            throw new PlatformNotSupportedException(
                $"Parakeet HTP model targets Snapdragon X Elite (Hexagon V73). Detected: {processor}. " +
                "Snapdragon X Plus may require a recompiled context binary.");
        }
    }

    private static string? TryReadProcessorName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorNameString")?.ToString()
                ?? key?.GetValue("VendorIdentifier")?.ToString();
        }
        catch
        {
            return null;
        }
    }
}
