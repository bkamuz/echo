using System.Runtime.Versioning;
using echo.Abstractions.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace echo.Engines.ParakeetNpu;

/// <summary>
/// Platform gating for Parakeet on Windows ARM64 (Snapdragon Copilot+ PCs).
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
        return SnapdragonProcessorMatcher.IsSupportedHtpProcessor(processorName);
    }

    public static void EnsureWindowsArm64()
    {
        if (!IsWindowsArm64)
        {
            throw new PlatformNotSupportedException(
                "Parakeet requires Windows on ARM64 (Snapdragon Copilot+ PC).");
        }
    }

    /// <summary>
    /// Logs HTP compatibility warnings but does not block load — runtime QNN errors are acceptable.
    /// </summary>
    public static void LogHtpCompatibilityWarning(ILogger? logger)
    {
        var processor = TryReadProcessorName() ?? string.Empty;
        if (logger is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(processor))
        {
            logger.LogWarning(
                "Could not read Snapdragon processor name. Parakeet HTP context targets Hexagon V73; load may fail.");
            return;
        }

        if (!SnapdragonProcessorMatcher.IsSupportedHtpProcessor(processor))
        {
            logger.LogWarning(
                "Parakeet HTP context targets Hexagon V73 (Snapdragon X Elite). Detected: {Processor}. " +
                "Unrecognized Snapdragon variant — attempting HTP anyway; X Plus may need a recompiled context binary.",
                processor);
            return;
        }

        if (SnapdragonProcessorMatcher.MayNeedAlternateHtpContext(processor))
        {
            logger.LogWarning(
                "Parakeet HTP context was built for Hexagon V73. Detected: {Processor}. " +
                "X2 Elite may need a different context binary if QNN load fails.",
                processor);
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
