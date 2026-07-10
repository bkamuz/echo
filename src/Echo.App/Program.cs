using Avalonia;
using echo.Platform.Linux;
using System;

namespace echo.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (OperatingSystem.IsLinux()
            && args.Contains(LinuxHotkeyBridge.Argument, StringComparer.Ordinal))
        {
            var bridgeIndex = Array.IndexOf(args, LinuxHotkeyBridge.Argument);
            var socketPath = bridgeIndex >= 0 && bridgeIndex + 1 < args.Length
                ? args[bridgeIndex + 1]
                : string.Empty;
            Environment.Exit(LinuxHotkeyBridge.Run(socketPath));
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace();
}
