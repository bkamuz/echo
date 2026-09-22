using System.Diagnostics;
using System.IO.Pipes;
using echo.Abstractions.Engines;
using Microsoft.Extensions.Logging;

namespace echo.Platform.Windows;

internal sealed class SherpaWorkerClient : IDisposable
{
    private readonly ILogger _logger;
    private readonly object _sync = new();
    private Process? _process;
    private NamedPipeServerStream? _pipe;
    private string _loadedDisplayName = string.Empty;

    public SherpaWorkerClient(ILogger logger)
    {
        _logger = logger;
    }

    public async Task EnsureReadyAsync(
        string engineId,
        EngineOptions options,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            EnsureConnectedUnsafe();
        }

        await SendConfigureAsync(engineId, options, cancellationToken).ConfigureAwait(false);
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
                _logger.LogDebug(ex, "Sherpa worker unload failed");
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
        var pipeName = $"Echo.SherpaWorker.{Guid.NewGuid():N}";
        _pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot resolve Echo executable path for Sherpa worker.");

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(SherpaWorkerBridge.Argument);
        startInfo.ArgumentList.Add(pipeName);

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start Sherpa worker process.");

        if (!WaitForConnection(_pipe, _process, TimeSpan.FromSeconds(20)))
        {
            var exitCode = _process.HasExited ? _process.ExitCode : -1;
            DisposeWorkerUnsafe();
            throw new InvalidOperationException(
                $"Sherpa worker did not connect (exit={exitCode}). Recognition engine failed to start on this device.");
        }

        _logger.LogInformation("Sherpa worker started (pid={Pid})", _process.Id);
    }

    private async Task SendConfigureAsync(
        string engineId,
        EngineOptions options,
        CancellationToken cancellationToken)
    {
        await WriteRequestAsync(
            SherpaWorkerCommand.Configure,
            SherpaWorkerProtocol.SerializeConfigure(engineId, options),
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
        DisposeWorkerUnsafe();
        var detail = exitCode is int code
            ? $"Sherpa worker exited unexpectedly (code={code})."
            : "Sherpa worker stopped unexpectedly.";
        _logger.LogWarning(ex, "{Detail}", detail);
        return new InvalidOperationException(
            "Не удалось загрузить движок распознавания на этом устройстве. Проверьте echo.log или попробуйте другой движок в настройках.",
            ex);
    }

    private static InvalidOperationException CreateWorkerUnavailableException() =>
        new("Sherpa worker is not connected.");

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
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(1000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to stop Sherpa worker process");
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }
}
