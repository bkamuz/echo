using System.Reflection;
using echo.Abstractions.Core;
using echo.Abstractions.Platform;

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

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                WriteMilestone("ProcessExit");
            };
        }
    }

    public static void WriteStartupContext()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            try
            {
                path = ApplicationLauncher.ResolveExecutablePath();
            }
            catch
            {
                path = "unknown";
            }
        }

        var entry = Assembly.GetEntryAssembly();
        var informational = entry?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var version = informational
            ?? entry?.GetName().Version?.ToString()
            ?? "unknown";

        WriteMilestone($"Process path={path} version={version}");
    }

    public static void WriteMilestone(string stage)
    {
        AppendLine(
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [Info] Startup: {stage}{Environment.NewLine}");
    }

    public static void WriteFatal(string source, Exception? exception)
    {
        var message = exception is null
            ? source
            : $"{source}{Environment.NewLine}{exception}";
        AppendLine(
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [Fatal] Startup: {message}{Environment.NewLine}");
        WriteCrashFile(source, exception);
    }

    /// <summary>Separate crash report (survives log rotation; easy to attach). Returns the path or null.</summary>
    public static string? WriteCrashFile(string source, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.CrashDir);
            var path = Path.Combine(AppPaths.CrashDir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
            var entry = Assembly.GetEntryAssembly();
            var version = entry?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
            File.WriteAllText(
                path,
                $"time={DateTimeOffset.Now:O}{Environment.NewLine}"
                + $"version={version}{Environment.NewLine}"
                + $"process={Environment.ProcessPath} pid={Environment.ProcessId}{Environment.NewLine}"
                + $"args={string.Join(' ', Environment.GetCommandLineArgs().Skip(1))}{Environment.NewLine}"
                + $"os={Environment.OSVersion} arch={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}{Environment.NewLine}"
                + $"source={source}{Environment.NewLine}{Environment.NewLine}{exception}{Environment.NewLine}");
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static void AppendLine(string line)
    {
        try
        {
            AppPaths.EnsureDirectories();

            lock (Gate)
            {
                using var stream = new FileStream(
                    AppPaths.LogPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite);
                using var writer = new StreamWriter(stream) { AutoFlush = true };
                writer.Write(line);
                stream.Flush(flushToDisk: true);
            }
        }
        catch
        {
            // Last-resort logging must never throw.
        }
    }
}
