using System.Reflection;

namespace echo.Platform.Linux;

internal static class LinuxHotkeyBridgeCommand
{
    public static (string FileName, IReadOnlyList<string> Arguments) Resolve(string socketPath)
    {
        var processPath = Environment.ProcessPath
            ?? Environment.GetCommandLineArgs().FirstOrDefault()
            ?? throw new InvalidOperationException("Cannot resolve process path.");

        if (IsDotnetHost(processPath))
        {
            var appDll = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(appDll) || !File.Exists(appDll))
            {
                throw new InvalidOperationException("Cannot resolve Echo.App assembly for hotkey bridge.");
            }

            return (processPath, [appDll, LinuxHotkeyBridge.Argument, socketPath]);
        }

        return (processPath, [LinuxHotkeyBridge.Argument, socketPath]);
    }

    private static bool IsDotnetHost(string path) =>
        string.Equals(Path.GetFileName(path), "dotnet", StringComparison.OrdinalIgnoreCase);
}
