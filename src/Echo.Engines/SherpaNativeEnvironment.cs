using System.Runtime.InteropServices;
using echo.Abstractions.Core;

namespace echo.Engines;

/// <summary>
/// Sherpa/ORT native environment helpers for engine load.
/// </summary>
public static class SherpaNativeEnvironment
{
    public static void PrepareForLoad() => SherpaNativeEnvironmentScrubber.PrepareForLoad();

    public static int ResolveNumThreads()
    {
        var cores = Math.Max(1, Environment.ProcessorCount);
        if (OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            return Math.Min(cores, 4);
        }

        return cores;
    }
}
