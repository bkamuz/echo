namespace echo.Platform.Linux;

public static class LinuxAccessibilityState
{
    public static bool IsEnabled => IsToolkitAccessibilityEnabled || IsScreenReaderEnabled;

    public static bool IsToolkitAccessibilityEnabled =>
        ReadGsettings("org.gnome.desktop.interface", "toolkit-accessibility") == "true";

    public static bool IsScreenReaderEnabled =>
        ReadGsettings("org.gnome.desktop.a11y.applications", "screen-reader-enabled") == "true";

    private static string? ReadGsettings(string schema, string key)
    {
        if (!LinuxSession.IsGnome || !LinuxCommandHelper.CommandExists("gsettings"))
        {
            return null;
        }

        try
        {
            return LinuxProcessRunner.RunCommand(
                "gsettings",
                ["get", schema, key],
                CancellationToken.None,
                allowFailure: true).Trim();
        }
        catch
        {
            return null;
        }
    }
}
