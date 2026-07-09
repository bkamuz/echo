using echo.Abstractions.Platform;

namespace echo.Platform.Linux;

public sealed class LinuxAutoStartService : IAutoStartService
{
    private const string DesktopFileName = "echo.desktop";

    public bool IsSupported => true;

    public bool IsEnabled => File.Exists(DesktopFilePath);

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            Directory.CreateDirectory(AutostartDirectory);
            var command = ApplicationLauncher.FormatLaunchCommand(ApplicationLauncher.ResolveExecutablePath());
            File.WriteAllText(DesktopFilePath, BuildDesktopEntry(command));
            return;
        }

        if (File.Exists(DesktopFilePath))
        {
            File.Delete(DesktopFilePath);
        }
    }

    private static string AutostartDirectory
    {
        get
        {
            var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var configRoot = string.IsNullOrWhiteSpace(xdgConfig)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
                : xdgConfig;
            return Path.Combine(configRoot, "autostart");
        }
    }

    private static string DesktopFilePath => Path.Combine(AutostartDirectory, DesktopFileName);

    private static string BuildDesktopEntry(string command) =>
        $"""
         [Desktop Entry]
         Type=Application
         Name=Echo
         Comment=Speech to text
         Exec={command}
         Terminal=false
         X-GNOME-Autostart-enabled=true
         """;
}
