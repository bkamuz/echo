using System.Runtime.InteropServices;

namespace echo.Platform.Windows;

/// <summary>
/// Sherpa/ORT native abort on Windows ARM64 is uncatchable in-process.
/// Route Sherpa engines through an isolated worker so the UI survives.
/// </summary>
public static class SherpaWorkerPolicy
{
    public static bool ShouldIsolate() =>
        OperatingSystem.IsWindows()
        && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

    public static bool IsSherpaEngineId(string engineId) =>
        engineId is "gigaam" or "whisper" or "omnilingual";
}
