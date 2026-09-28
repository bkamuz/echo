using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using echo.Abstractions.Core;
using echo.Abstractions.Engines;
using echo.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace echo.Platform.Windows;

internal sealed class ParakeetQnnWorkerClient : IDisposable
{
    private readonly ILogger _logger;
    private readonly object _sync = new();
    private Process? _process;
    private NamedPipeServerStream? _pipe;
    private StringBuilder? _workerStderr;
    private string _loadedDisplayName = string.Empty;

    public ParakeetQnnWorkerClient(ILogger logger)
    {
        _logger = logger;
    }

    public async Task EnsureReadyAsync(EngineOptions options, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            EnsureConnectedUnsafe();
        }

        await SendConfigureAsync(options, cancellationToken).ConfigureAwait(false);
        var displayName = await SendEnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        _loadedDisplayName = displayName;
    }

    public async Task<string> TranscribeAsync(
        float[] samples,
        int sampleRate,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            EnsureConnectedUnsafe();
        }

        await WriteRequestAsync(
            SherpaWorkerCommand.Transcribe,
            SherpaWorkerProtocol.SerializeTranscribe(sampleRate, samples),
            cancellationToken).ConfigureAwait(false);
        return await ReadTextResponseAsync(cancellationToken).ConfigureAwait(false);
    }

    public string LoadedDisplayName => _loadedDisplayName;

    public void Unload()
    {
        lock (_sync)
        {
            if (_pipe is null)
            {
                return;
            }

            try
            {
                WriteRequestAsync(SherpaWorkerCommand.Unload, [], CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                ReadResponseAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Parakeet QNN worker unload failed");
            }
            finally
            {
                DisposeWorkerUnsafe();
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            DisposeWorkerUnsafe();
        }
    }

    private void EnsureConnectedUnsafe()
    {
        if (_pipe is not null && _process is { HasExited: false })
        {
            return;
        }

        DisposeWorkerUnsafe();
        StartWorkerUnsafe();
    }

    private void StartWorkerUnsafe()
    {
        var pipeName = $"Echo.ParakeetQnnWorker.{Guid.NewGuid():N}";
        _pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot resolve Echo executable path for Parakeet QNN worker.");

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(ParakeetQnnWorkerBridge.Argument);
        startInfo.ArgumentList.Add(pipeName);
        SherpaNativeEnvironmentScrubber.PrepareForLoad(startInfo.Environment);

        _workerStderr = new StringBuilder();
        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start Parakeet QNN worker process.");
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                _workerStderr.AppendLine(e.Data);
            }
        };
        _process.BeginErrorReadLine();

        if (!WaitForConnection(_pipe, _process, TimeSpan.FromSeconds(120)))
        {
            var exitCode = _process.HasExited ? _process.ExitCode : -1;
            LogWorkerExit(exitCode, "Parakeet QNN worker did not connect");
            DisposeWorkerUnsafe();
            throw new InvalidOperationException(
                $"Parakeet QNN worker did not connect (exit={exitCode}). NPU path failed to start; use CPU or check echo-parakeet-qnn-worker.log.");
        }

        _logger.LogInformation(
            "Parakeet QNN worker started (pid={Pid}, log={WorkerLog})",
            _process.Id,
            AppPaths.ParakeetQnnWorkerLogPath);
    }

    private async Task SendConfigureAsync(EngineOptions options, CancellationToken cancellationToken)
    {
        await WriteRequestAsync(
            SherpaWorkerCommand.Configure,
            SherpaWorkerProtocol.SerializeConfigure("parakeet_npu", options),
            cancellationToken).ConfigureAwait(false);
        await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> SendEnsureLoadedAsync(CancellationToken cancellationToken)
    {
        await WriteRequestAsync(SherpaWorkerCommand.EnsureLoaded, [], cancellationToken).ConfigureAwait(false);
        return await ReadTextResponseAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteRequestAsync(
        SherpaWorkerCommand command,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var pipe = _pipe ?? throw CreateWorkerUnavailableException();
        try
        {
            await SherpaWorkerProtocol.WriteRequestAsync(pipe, command, payload, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException)
        {
            throw WrapWorkerFailure(ex);
        }
    }

    private async Task ReadResponseAsync(CancellationToken cancellationToken)
    {
        var pipe = _pipe ?? throw CreateWorkerUnavailableException();
        try
        {
            var (status, payload) = await SherpaWorkerProtocol.ReadResponseAsync(pipe, cancellationToken)
                .ConfigureAwait(false);
            if (status == SherpaWorkerStatus.Error)
            {
                throw new InvalidOperationException(SherpaWorkerProtocol.DeserializeError(payload));
            }
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException)
        {
            throw WrapWorkerFailure(ex);
        }
    }

    private async Task<string> ReadTextResponseAsync(CancellationToken cancellationToken)
    {
        var pipe = _pipe ?? throw CreateWorkerUnavailableException();
        try
        {
            var (status, payload) = await SherpaWorkerProtocol.ReadResponseAsync(pipe, cancellationToken)
                .ConfigureAwait(false);
            if (status == SherpaWorkerStatus.Error)
            {
                throw new InvalidOperationException(SherpaWorkerProtocol.DeserializeError(payload));
            }

            return SherpaWorkerProtocol.DeserializeText(payload);
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException)
        {
            throw WrapWorkerFailure(ex);
        }
    }

    private InvalidOperationException WrapWorkerFailure(Exception ex)
    {
        var exitCode = _process is { HasExited: true } ? _process.ExitCode : (int?)null;
        LogWorkerExit(exitCode, "Parakeet QNN worker stopped unexpectedly");
        DisposeWorkerUnsafe();
        var detail = exitCode is int code
            ? $"Parakeet QNN worker exited unexpectedly ({NativeExitCodes.Describe(code)})."
            : "Parakeet QNN worker stopped unexpectedly.";
        _logger.LogWarning(ex, "{Detail}", detail);
        return new InvalidOperationException(
            "Parakeet NPU worker failed (native crash or load error). See echo.log and echo-parakeet-qnn-worker.log; switch to CPU in Settings.",
            ex);
    }

    private void LogWorkerExit(int? exitCode, string reason)
    {
        var codeText = exitCode is int code
            ? NativeExitCodes.Describe(code)
            : "still running or unknown";
        var stderr = _workerStderr?.ToString().Trim();
        var workerTail = ParakeetQnnWorkerDiagnostics.TryReadTail();
        _logger.LogWarning(
            "{Reason}. exit={ExitCode} ({CodeText}). workerLog={WorkerLog}. workerTail={WorkerTail}. stderr={Stderr}",
            reason,
            exitCode,
            codeText,
            AppPaths.ParakeetQnnWorkerLogPath,
            workerTail,
            string.IsNullOrWhiteSpace(stderr) ? "(empty)" : stderr);
    }

    private static InvalidOperationException CreateWorkerUnavailableException() =>
        new("Parakeet QNN worker is not connected.");

    private static bool WaitForConnection(NamedPipeServerStream pipe, Process process, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            pipe.WaitForConnectionAsync(cts.Token).GetAwaiter().GetResult();
            return pipe.IsConnected;
        }
        catch (OperationCanceledException)
        {
            return pipe.IsConnected && !process.HasExited;
        }
    }

    private void DisposeWorkerUnsafe()
    {
        try
        {
            _pipe?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _pipe = null;
        }

        if (_process is null)
        {
            _workerStderr = null;
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(1000);
            }

            if (_process.HasExited)
            {
                LogWorkerExit(_process.ExitCode, "Parakeet QNN worker disposed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to stop Parakeet QNN worker process");
        }
        finally
        {
            _process.Dispose();
            _process = null;
            _workerStderr = null;
        }
    }
}
