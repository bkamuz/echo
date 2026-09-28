using System.IO.Pipes;
using echo.Abstractions.Core;
using echo.Abstractions.Engines;
using echo.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace echo.Platform.Windows;

/// <summary>
/// Headless Parakeet QNN worker entry point (Windows ARM64 process isolation).
/// </summary>
public static class ParakeetQnnWorkerBridge
{
    public const string Argument = "--parakeet-qnn-worker";

    public static int Run(string pipeName, ILoggerFactory? loggerFactory = null)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            Console.Error.WriteLine("parakeet-qnn-worker: pipe name is required");
            return 1;
        }

        AppPaths.EnsureDirectories();
        ParakeetQnnWorkerDiagnostics.WriteMilestone(
            $"ParakeetQnnWorkerBridge.Run enter env={SherpaNativeEnvironmentScrubber.DescribeSnapshot()}");

        using var ownedLoggerFactory = loggerFactory is null ? CreateDefaultLoggerFactory() : null;
        loggerFactory ??= ownedLoggerFactory!;

        var logger = loggerFactory.CreateLogger("ParakeetQnnWorker");
        ITranscriptionEngine? engine = null;
        EngineOptions currentOptions = new();

        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            ParakeetQnnWorkerDiagnostics.WriteMilestone("before pipe.Connect");
            pipe.Connect(15_000);
            ParakeetQnnWorkerDiagnostics.WriteMilestone($"pipe connected name={pipeName}");
            logger.LogInformation("Connected to Parakeet QNN worker pipe {Pipe}", pipeName);

            while (true)
            {
                var request = ReadRequest(pipe);
                switch (request.Command)
                {
                    case SherpaWorkerCommand.Configure:
                    {
                        var configure = SherpaWorkerProtocol.DeserializeConfigure(request.Payload);
                        if (!string.Equals(configure.EngineId, "parakeet_npu", StringComparison.Ordinal))
                        {
                            WriteError(pipe, $"Parakeet QNN worker only supports parakeet_npu, got '{configure.EngineId}'.");
                            break;
                        }

                        if (engine is null || !OptionsEqual(currentOptions, configure.Options))
                        {
                            engine?.Unload();
                            engine = ParakeetAssemblyLoader.CreateInProcessEngineForWorker(loggerFactory);
                            currentOptions = CloneOptions(configure.Options);
                        }

                        engine!.Configure(currentOptions);
                        WriteOk(pipe, []);
                        break;
                    }

                    case SherpaWorkerCommand.EnsureLoaded:
                    {
                        try
                        {
                            EnsureEngineReady(engine);
                            ParakeetQnnWorkerDiagnostics.WriteMilestone(
                                $"EnsureLoaded begin device={currentOptions.Device} env={SherpaNativeEnvironmentScrubber.DescribeSnapshot()}");
                            logger.LogInformation(
                                "EnsureLoaded begin device={Device} env={Env}",
                                currentOptions.Device,
                                SherpaNativeEnvironmentScrubber.DescribeSnapshot());
                            engine!.EnsureLoadedAsync().GetAwaiter().GetResult();
                            ParakeetQnnWorkerDiagnostics.WriteMilestone("EnsureLoaded done");
                            WriteOk(pipe, SherpaWorkerProtocol.SerializeText(engine.DisplayName));
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Parakeet QNN worker EnsureLoaded failed");
                            WriteError(pipe, ex.Message);
                        }

                        break;
                    }

                    case SherpaWorkerCommand.Transcribe:
                    {
                        try
                        {
                            EnsureEngineReady(engine);
                            var (sampleRate, samples) = SherpaWorkerProtocol.DeserializeTranscribe(request.Payload);
                            ParakeetQnnWorkerDiagnostics.WriteMilestone(
                                $"Transcribe begin samples={samples.Length} rate={sampleRate}");
                            var text = engine!.TranscribeAsync(samples, sampleRate).GetAwaiter().GetResult();
                            ParakeetQnnWorkerDiagnostics.WriteMilestone($"Transcribe done chars={text.Length}");
                            WriteOk(pipe, SherpaWorkerProtocol.SerializeText(text));
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Parakeet QNN worker Transcribe failed");
                            WriteError(pipe, ex.Message);
                        }

                        break;
                    }

                    case SherpaWorkerCommand.Unload:
                    {
                        engine?.Unload();
                        WriteOk(pipe, []);
                        break;
                    }

                    case SherpaWorkerCommand.Ping:
                        WriteOk(pipe, []);
                        break;

                    default:
                        WriteError(pipe, $"Unsupported Parakeet QNN worker command {(byte)request.Command}.");
                        break;
                }
            }
        }
        catch (EndOfStreamException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            ParakeetQnnWorkerDiagnostics.WriteFatal("Parakeet QNN worker failed", ex);
            logger.LogError(ex, "Parakeet QNN worker failed");
            return 1;
        }
        finally
        {
            engine?.Unload();
        }
    }

    private static ILoggerFactory CreateDefaultLoggerFactory() =>
        LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
            builder.SetMinimumLevel(LogLevel.Information);
        });

    private static void EnsureEngineReady(ITranscriptionEngine? engine)
    {
        if (engine is null)
        {
            throw new InvalidOperationException("Parakeet QNN worker received a command before configure.");
        }
    }

    private static (SherpaWorkerCommand Command, byte[] Payload) ReadRequest(Stream stream)
    {
        var header = ReadExact(stream, 6);
        if (header[0] != 1)
        {
            throw new InvalidOperationException($"Unsupported worker protocol version {header[0]}.");
        }

        var command = (SherpaWorkerCommand)header[1];
        var length = BitConverter.ToInt32(header, 2);
        if (length < 0)
        {
            throw new InvalidOperationException("Parakeet QNN worker received a negative payload length.");
        }

        var payload = length == 0 ? [] : ReadExact(stream, length);
        return (command, payload);
    }

    private static byte[] ReadExact(Stream stream, int length)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = stream.Read(buffer, offset, length - offset);
            if (read == 0)
            {
                throw new EndOfStreamException("Parakeet QNN worker pipe closed unexpectedly.");
            }

            offset += read;
        }

        return buffer;
    }

    private static void WriteOk(Stream stream, byte[] payload) =>
        SherpaWorkerProtocol.WriteResponseAsync(stream, SherpaWorkerStatus.Ok, payload, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    private static void WriteError(Stream stream, string message) =>
        SherpaWorkerProtocol.WriteResponseAsync(
                stream,
                SherpaWorkerStatus.Error,
                SherpaWorkerProtocol.SerializeError(message),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    private static bool OptionsEqual(EngineOptions left, EngineOptions right) =>
        string.Equals(left.Engine, right.Engine, StringComparison.Ordinal)
        && string.Equals(left.WhisperModelSize, right.WhisperModelSize, StringComparison.Ordinal)
        && string.Equals(left.GigaAmModelSize, right.GigaAmModelSize, StringComparison.Ordinal)
        && string.Equals(left.Language, right.Language, StringComparison.Ordinal)
        && string.Equals(left.Device, right.Device, StringComparison.Ordinal)
        && left.SampleRate == right.SampleRate;

    private static EngineOptions CloneOptions(EngineOptions options) => new()
    {
        Engine = options.Engine,
        WhisperModelSize = options.WhisperModelSize,
        GigaAmModelSize = options.GigaAmModelSize,
        Language = options.Language,
        Device = options.Device,
        SampleRate = options.SampleRate,
    };
}
