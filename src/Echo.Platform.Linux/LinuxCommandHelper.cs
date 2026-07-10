namespace echo.Platform.Linux;

internal static class LinuxCommandHelper
{
    public static bool CommandExists(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        foreach (var dir in path.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (File.Exists(Path.Combine(dir, command)))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsFlatpakSandbox() => File.Exists("/.flatpak-info");
}
