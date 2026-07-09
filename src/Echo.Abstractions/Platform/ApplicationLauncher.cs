namespace echo.Abstractions.Platform;

public static class ApplicationLauncher
{
    public const string MinimizedArgument = "--minimized";

    public static string ResolveExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return Environment.ProcessPath;
        }

        var name = OperatingSystem.IsWindows() ? "Echo.App.exe" : "Echo.App";
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, name));
    }

    public static string FormatLaunchCommand(string executablePath) =>
        $"\"{executablePath}\" {MinimizedArgument}";
}
