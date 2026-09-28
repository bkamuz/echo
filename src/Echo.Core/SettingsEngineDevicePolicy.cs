using echo.Abstractions.Engines;

namespace echo.Core;

/// <summary>
/// Engine ↔ compute-device capability matrix for settings UI and config normalization.
/// </summary>
public static class SettingsEngineDevicePolicy
{
    private static readonly string[] DevicePreferenceOrder =
    [
        ExecutionProviderResolver.CpuDevice,
        ExecutionProviderResolver.DirectMlDevice,
        ExecutionProviderResolver.NpuDevice,
    ];

    private static readonly Dictionary<string, HashSet<string>> EngineDevices = new(StringComparer.Ordinal)
    {
        ["gigaam"] = new(StringComparer.Ordinal)
        {
            ExecutionProviderResolver.CpuDevice,
            ExecutionProviderResolver.DirectMlDevice,
        },
        ["whisper"] = new(StringComparer.Ordinal)
        {
            ExecutionProviderResolver.CpuDevice,
        },
        ["omnilingual"] = new(StringComparer.Ordinal)
        {
            ExecutionProviderResolver.CpuDevice,
            ExecutionProviderResolver.DirectMlDevice,
        },
        ["parakeet_npu"] = new(StringComparer.Ordinal)
        {
            ExecutionProviderResolver.CpuDevice,
            ExecutionProviderResolver.NpuDevice,
        },
    };

    public static bool SupportsDevice(string engine, string device)
    {
        var deviceId = NormalizeDeviceId(device);
        if (!EngineDevices.TryGetValue(engine, out var supported))
        {
            return deviceId == ExecutionProviderResolver.CpuDevice;
        }

        return supported.Contains(deviceId);
    }

    public static IReadOnlyList<string> FilterEnginesForDevice(
        IEnumerable<string> orderedEngineIds,
        string device)
    {
        var deviceId = NormalizeDeviceId(device);
        return orderedEngineIds.Where(id => SupportsDevice(id, deviceId)).ToList();
    }

    public static string ResolveEngineForDevice(
        string currentEngine,
        string device,
        IReadOnlyList<string> orderedEngineIds)
    {
        var candidates = FilterEnginesForDevice(orderedEngineIds, device);
        if (candidates.Count == 0)
        {
            return currentEngine;
        }

        if (SupportsDevice(currentEngine, device))
        {
            return currentEngine;
        }

        return candidates[0];
    }

    public static string NormalizeDeviceForEngine(string engine, string device)
    {
        var deviceId = NormalizeDeviceId(device);
        if (SupportsDevice(engine, deviceId))
        {
            return deviceId;
        }

        foreach (var preferred in DevicePreferenceOrder)
        {
            if (SupportsDevice(engine, preferred))
            {
                return preferred;
            }
        }

        return ExecutionProviderResolver.CpuDevice;
    }

    private static string NormalizeDeviceId(string device)
    {
        return ExecutionProviderResolver.ToConfigDevice(
            ExecutionProviderResolver.FromConfigDevice(device));
    }
}
