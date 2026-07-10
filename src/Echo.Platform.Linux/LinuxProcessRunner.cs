using System.Diagnostics;
using System.Text;

namespace echo.Platform.Linux;

internal static class LinuxProcessRunner
{
    private const int DefaultTimeoutMs = 5000;

    public static string RunCommand(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool allowFailure = false,
        int timeoutMs = DefaultTimeoutMs)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = CreateStartInfo(fileName, arguments, redirectOutput: allowFailure);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {fileName}.");

        var (output, error) = allowFailure
            ? ReadOutputs(process, cancellationToken, timeoutMs)
            : (string.Empty, string.Empty);

        if (process.ExitCode != 0 && !allowFailure)
        {
            throw new InvalidOperationException($"{fileName} exited with code {process.ExitCode}. {error}");
        }

        return output;
    }

    public static int RunCommand(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool allowFailure,
        out string stderr,
        int timeoutMs = DefaultTimeoutMs)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = CreateStartInfo(fileName, arguments, redirectOutput: true);
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            stderr = string.Empty;
            return 1;
        }

        (_, stderr) = ReadOutputs(process, cancellationToken, timeoutMs);
        return process.ExitCode;
    }

    public static void RunCommandWithInput(
        string fileName,
        IReadOnlyList<string> arguments,
        string input,
        CancellationToken cancellationToken,
        int timeoutMs = DefaultTimeoutMs)
    {
        var exitCode = RunCommandWithInputExitCode(fileName, arguments, input, cancellationToken, timeoutMs);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} exited with code {exitCode}.");
        }
    }

    public static int RunCommandWithInputExitCode(
        string fileName,
        IReadOnlyList<string> arguments,
        string input,
        CancellationToken cancellationToken,
        int timeoutMs = DefaultTimeoutMs)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = CreateStartInfo(fileName, arguments, redirectInput: true);
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return 1;
        }

        process.StandardInput.Write(input);
        process.StandardInput.Close();
        WaitForProcess(process, cancellationToken, timeoutMs);
        return process.ExitCode;
    }

    private static ProcessStartInfo CreateStartInfo(
        string fileName,
        IReadOnlyList<string> arguments,
        bool redirectInput = false,
        bool redirectOutput = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardInput = redirectInput,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = redirectOutput,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (redirectOutput)
        {
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static (string stdout, string stderr) ReadOutputs(
        Process process,
        CancellationToken cancellationToken,
        int timeoutMs)
    {
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        WaitForProcess(process, cancellationToken, timeoutMs);

        try
        {
            Task.WaitAll([stdoutTask, stderrTask], cancellationToken);
        }
        catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return (stdoutTask.Result, stderrTask.Result);
    }

    private static void WaitForProcess(Process process, CancellationToken cancellationToken, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!process.HasExited)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Environment.TickCount64 >= deadline)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                throw new TimeoutException($"{process.StartInfo.FileName} timed out after {timeoutMs}ms.");
            }

            process.WaitForExit(100);
        }

        process.WaitForExit();
    }
}
