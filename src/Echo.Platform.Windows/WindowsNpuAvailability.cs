using System.Runtime.InteropServices;
using echo.Abstractions.Core;
using echo.Abstractions.Platform;
using Microsoft.Win32;

namespace echo.Platform.Windows;

public sealed class WindowsNpuAvailability : INpuAvailability
{
    public bool IsLikelyPlatform =>
        OperatingSystem.IsWindows()
        && RuntimeInformation.ProcessArchitecture is Architecture.Arm64 or Architecture.Arm;

    public bool IsHardwareDetected => IsLikelyPlatform && ProbeHardware(out _);

    public bool IsAvailable => IsLikelyPlatform && IsSnapdragonXElite();

    public string StatusDetail
    {
        get
        {
            if (!IsLikelyPlatform)
            {
                return "NPU acceleration requires Windows on ARM64 (Snapdragon).";
            }

            if (NpuPaths.IsInstalled)
            {
                return "QNN runtime is installed.";
            }

            return ProbeHardware(out var detail)
                ? detail
                : "Snapdragon NPU not detected on this ARM64 PC.";
        }
    }

    internal static bool IsSnapdragonXElite()
    {
        return ProbeHardware(out _) && TryReadProcessorName(out var name) && IsXEliteName(name);
    }

    internal static bool ProbeHardware(out string detail)
    {
        detail = string.Empty;

        if (TryFindQnnHtp(out var qnnPath))
        {
            detail = $"QNN HTP library found ({qnnPath}).";
            return true;
        }

        if (TryReadProcessorVendor(out var vendor) && vendor.Contains("qualcomm", StringComparison.OrdinalIgnoreCase))
        {
            detail = $"Qualcomm processor detected ({vendor}).";
            return true;
        }

        if (TryReadPlatformRole(out var role)
            && role.Contains("snapdragon", StringComparison.OrdinalIgnoreCase))
        {
            detail = $"Snapdragon platform role ({role}).";
            return true;
        }

        detail = "No QNN HTP library or Qualcomm CPU identifier found.";
        return false;
    }

    private static bool TryFindQnnHtp(out string path)
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "QnnHtp.dll"),
            Path.Combine(AppContext.BaseDirectory, "qnn", "QnnHtp.dll"),
            Path.Combine(NpuPaths.UserDir, "QnnHtp.dll"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                path = candidate;
                return true;
            }
        }

        path = string.Empty;
        return false;
    }

    private static bool TryReadProcessorName(out string name) =>
        TryReadProcessorVendor(out name);

    private static bool IsXEliteName(string processor)
    {
        var normalized = processor.ToLowerInvariant();
        return normalized.Contains("snapdragon")
            && (normalized.Contains("x elite") || normalized.Contains("x1e"));
    }

    private static bool TryReadProcessorVendor(out string vendor)
    {
        vendor = string.Empty;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            vendor = key?.GetValue("ProcessorNameString")?.ToString()
                ?? key?.GetValue("VendorIdentifier")?.ToString()
                ?? string.Empty;
            return !string.IsNullOrWhiteSpace(vendor);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadPlatformRole(out string role)
    {
        role = string.Empty;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SystemInformation");
            role = key?.GetValue("SystemProductName")?.ToString()
                ?? key?.GetValue("SystemFamily")?.ToString()
                ?? string.Empty;
            return !string.IsNullOrWhiteSpace(role);
        }
        catch
        {
            return false;
        }
    }
}
