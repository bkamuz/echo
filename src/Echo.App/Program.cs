using Avalonia;
using echo.Abstractions.Core;
using echo.App.Logging;
using echo.Core.Diagnostics;
using echo.Platform.Linux;
using echo.Platform.Windows;
using Microsoft.Extensions.Logging;
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
        StartupDiagnostics.RegisterUnhandledExceptionHandlers();
        StartupDiagnostics.WriteMilestone("Program.Main enter");
        StartupDiagnostics.WriteStartupContext();

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

        if (OperatingSystem.IsWindows()
            && args.Contains(SherpaWorkerBridge.Argument, StringComparer.Ordinal))
        {
            var workerIndex = Array.IndexOf(args, SherpaWorkerBridge.Argument);
            var pipeName = workerIndex >= 0 && workerIndex + 1 < args.Length
                ? args[workerIndex + 1]
                : string.Empty;
            AppPaths.EnsureDirectories();
            StartupDiagnostics.WriteMilestone("Sherpa worker mode enter");
            StartupDiagnostics.WriteMilestone(
                $"Sherpa worker env before scrub: {SherpaNativeEnvironmentScrubber.DescribeSnapshot()}");
            SherpaNativeEnvironmentScrubber.PrepareForLoad();
            StartupDiagnostics.WriteMilestone(
                $"Sherpa worker env after scrub: {SherpaNativeEnvironmentScrubber.DescribeSnapshot()}");
            using var workerLogFactory = LoggerFactory.Create(builder =>
            {
                builder.AddProvider(new FileLoggerProvider(AppPaths.LogPath));
                builder.SetMinimumLevel(LogLevel.Information);
            });
            Environment.Exit(SherpaWorkerBridge.Run(pipeName, workerLogFactory));
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteFatal("Avalonia startup failed", ex);
            throw;
        }
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
