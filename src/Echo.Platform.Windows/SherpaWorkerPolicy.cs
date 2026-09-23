namespace echo.Platform.Windows;

/// <summary>
/// Sherpa engine routing on Windows. GigaAM/Sherpa runs in-process on all
/// Windows targets (including ARM64), matching stable v1.9.7 behavior.
/// Out-of-process worker isolation (v1.9.9+) AV'd on Snapdragon load.
/// <see cref="echo.Abstractions.Core.SherpaNativeEnvironmentScrubber"/> is kept
/// for the optional worker path only — do not scrub before in-process Sherpa load.
/// </summary>
public static class SherpaWorkerPolicy
{
    /// <summary>
    /// When true, Sherpa engines load in a child process. Disabled — in-process
    /// was stable on win-arm64 through v1.9.7; the worker path regressed load.
    /// </summary>
    public static bool ShouldIsolate() => false;

    public static bool IsSherpaEngineId(string engineId) =>
        engineId is "gigaam" or "whisper" or "omnilingual";
}
