namespace echo.Core;

public static class StartupWarmupPolicy
{
    /// <summary>
    /// Sherpa/ORT native abort on Windows is uncatchable; defer engine load until
    /// first dictation or an explicit settings apply so UI/tray can appear first.
    /// </summary>
    public static bool ShouldDeferWarmup(bool isWindows) => isWindows;
}
