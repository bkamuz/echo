using echo.Abstractions.Core;

namespace echo.Core.Diagnostics;

/// <summary>
/// Writes fatal startup failures to echo.log before DI/logging host is available.
/// </summary>
public static class StartupDiagnostics
{
    private static readonly object Gate = new();
    private static bool _registered;

    public static void RegisterUnhandledExceptionHandlers()
    {
        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            _registered = true;
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                WriteFatal("AppDomain.UnhandledException", args.ExceptionObject as Exception);
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                WriteFatal("TaskScheduler.UnobservedTaskException", args.Exception);
                args.SetObserved();
            };
        }
    }

    public static void WriteFatal(string source, Exception? exception)
    {
        try
        {
            AppPaths.EnsureDirectories();
            var message = exception is null
                ? source
                : $"{source}{Environment.NewLine}{exception}";
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [Fatal] Startup: {message}{Environment.NewLine}";

            lock (Gate)
            {
                File.AppendAllText(AppPaths.LogPath, line);
            }
        }
        catch
        {
            // Last-resort logging must never throw.
        }
    }
}
