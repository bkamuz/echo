using System.Reflection;
using System.Runtime.InteropServices;
using echo.Abstractions.Core;

namespace echo.Core.Diagnostics;

/// <summary>
/// Worker-only startup log (%APPDATA%\Echo\echo-sherpa-worker.log) so parent/worker
/// races on echo.log do not hide where the Sherpa child died.
/// </summary>
public static class SherpaWorkerDiagnostics
{
    public const string WorkerArgument = "--sherpa-worker";

    private static readonly object Gate = new();
    private static bool _registered;

    public static bool IsWorkerProcess()
    {
        try
        {
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (string.Equals(arg, WorkerArgument, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Best-effort detection only.
        }

        return false;
    }

    public static void RegisterUnhandledExceptionHandlersIfWorker()
    {
        if (!IsWorkerProcess())
        {
            return;
        }

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

    public static void WriteProcessStart(string stage)
    {
        var path = SafeProcessPath();
        var entry = Assembly.GetEntryAssembly();
        var informational = entry?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var version = informational
            ?? entry?.GetName().Version?.ToString()
            ?? "unknown";

        WriteMilestone(
            $"{stage} pid={Environment.ProcessId} arch={RuntimeInformation.ProcessArchitecture} path={path} version={version}");
        WriteMilestone($"args={FormatCommandLine()}");
    }

    public static void WriteMilestone(string stage)
    {
        AppendLine(
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [Info] Worker: {stage}{Environment.NewLine}");
    }

    public static void WriteFatal(string source, Exception? exception)
    {
        var message = exception is null
            ? source
            : $"{source}{Environment.NewLine}{exception}";
        AppendLine(
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [Fatal] Worker: {message}{Environment.NewLine}");
    }

    public static string TryReadTail(int maxChars = 4096)
    {
        try
        {
            if (!File.Exists(AppPaths.SherpaWorkerLogPath))
            {
                return "(worker log not created — child likely died before managed bootstrap)";
            }

            var text = File.ReadAllText(AppPaths.SherpaWorkerLogPath);
            if (text.Length <= maxChars)
            {
                return text;
            }

            return text[^maxChars..];
        }
        catch (Exception ex)
        {
            return $"(failed to read worker log: {ex.Message})";
        }
    }

    private static string SafeProcessPath()
    {
        try
        {
            return Environment.ProcessPath ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static string FormatCommandLine()
    {
        try
        {
            return string.Join(' ', Environment.GetCommandLineArgs());
        }
        catch
        {
            return "unknown";
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
                    AppPaths.SherpaWorkerLogPath,
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
