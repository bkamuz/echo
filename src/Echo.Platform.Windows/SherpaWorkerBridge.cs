using System.IO.Pipes;
using echo.Abstractions.Core;
using echo.Abstractions.Engines;
using echo.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace echo.Platform.Windows;

/// <summary>
/// Headless Sherpa worker entry point for Windows ARM64 process isolation.
/// </summary>
public static class SherpaWorkerBridge
{
    public const string Argument = "--sherpa-worker";

    private static readonly Dictionary<string, string> EngineTypeNames = new(StringComparer.Ordinal)
    {
        ["gigaam"] = "echo.Engines.GigaAm.GigaAmEngine",
        ["omnilingual"] = "echo.Engines.Omnilingual.OmnilingualEngine",
        ["whisper"] = "echo.Engines.Whisper.WhisperEngine",
    };

    public static int Run(string pipeName, ILoggerFactory? loggerFactory = null)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            Console.Error.WriteLine("sherpa-worker: pipe name is required");
            return 1;
        }

        AppPaths.EnsureDirectories();
        SherpaNativeEnvironmentScrubber.PrepareForLoad();
        SherpaWorkerDiagnostics.WriteMilestone(
            $"SherpaWorkerBridge.Run enter env={SherpaNativeEnvironmentScrubber.DescribeSnapshot()}");

        using var ownedLoggerFactory = loggerFactory is null ? CreateDefaultLoggerFactory() : null;
        loggerFactory ??= ownedLoggerFactory!;

        var logger = loggerFactory.CreateLogger("SherpaWorker");
        ITranscriptionEngine? engine = null;
        EngineOptions currentOptions = new();
        var currentEngineId = string.Empty;

        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            SherpaWorkerDiagnostics.WriteMilestone("before pipe.Connect");
            pipe.Connect(15_000);
            SherpaWorkerDiagnostics.WriteMilestone($"pipe connected name={pipeName}");
            logger.LogInformation("Connected to Sherpa worker pipe {Pipe}", pipeName);

            while (true)
            {
                var request = ReadRequest(pipe);
                switch (request.Command)
                {
                    case SherpaWorkerCommand.Configure:
                    {
                        var configure = SherpaWorkerProtocol.DeserializeConfigure(request.Payload);
                        if (!string.Equals(currentEngineId, configure.EngineId, StringComparison.Ordinal)
                            || !OptionsEqual(currentOptions, configure.Options))
                        {
                            engine?.Unload();
                            engine = CreateEngine(loggerFactory, configure.EngineId);
                            currentEngineId = configure.EngineId;
                            currentOptions = CloneOptions(configure.Options);
                        }

                        engine!.Configure(currentOptions);
                        WriteOk(pipe, []);
                        break;
                    }

                    case SherpaWorkerCommand.EnsureLoaded:
                    {
                        EnsureEngineReady(engine);
                        SherpaWorkerDiagnostics.WriteMilestone(
                            $"EnsureLoaded begin engine={currentEngineId} device={currentOptions.Device} gigaam={currentOptions.GigaAmModelSize} env={SherpaNativeEnvironmentScrubber.DescribeSnapshot()}");
                        logger.LogInformation(
                            "EnsureLoaded begin engine={EngineId} device={Device} gigaam={GigaAmModelSize} env={Env}",
                            currentEngineId,
                            currentOptions.Device,
                            currentOptions.GigaAmModelSize,
                            SherpaNativeEnvironmentScrubber.DescribeSnapshot());
                        engine!.EnsureLoadedAsync().GetAwaiter().GetResult();
                        SherpaWorkerDiagnostics.WriteMilestone(
                            $"EnsureLoaded done engine={currentEngineId}");
                        WriteOk(pipe, SherpaWorkerProtocol.SerializeText(engine.DisplayName));
                        break;
                    }

                    case SherpaWorkerCommand.Transcribe:
                    {
                        EnsureEngineReady(engine);
                        var (sampleRate, samples) = SherpaWorkerProtocol.DeserializeTranscribe(request.Payload);
                        var text = engine!.TranscribeAsync(samples, sampleRate).GetAwaiter().GetResult();
                        WriteOk(pipe, SherpaWorkerProtocol.SerializeText(text));
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
                        WriteError(pipe, $"Unsupported Sherpa worker command {(byte)request.Command}.");
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
            SherpaWorkerDiagnostics.WriteFatal("Sherpa worker failed", ex);
            logger.LogError(ex, "Sherpa worker failed");
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

    private static ITranscriptionEngine CreateEngine(ILoggerFactory loggerFactory, string engineId)
    {
        if (!EngineTypeNames.TryGetValue(engineId, out var typeName))
        {
            throw new InvalidOperationException($"Engine '{engineId}' is not supported by the Sherpa worker.");
        }

        SherpaWorkerDiagnostics.WriteMilestone($"before LoadAssemblyForWorker engine={engineId} type={typeName}");
        var assembly = SherpaEnginesAssemblyLoader.LoadAssemblyForWorker();
        SherpaWorkerDiagnostics.WriteMilestone($"after LoadAssemblyForWorker engine={engineId} assembly={assembly.FullName}");
        SherpaWorkerDiagnostics.WriteMilestone($"before CreateEngine engine={engineId}");
        var engine = ReflectionEngineFactory.CreateEngine(loggerFactory, assembly, typeName);
        SherpaWorkerDiagnostics.WriteMilestone($"after CreateEngine engine={engineId}");
        return engine;
    }

    private static void EnsureEngineReady(ITranscriptionEngine? engine)
    {
        if (engine is null)
        {
            throw new InvalidOperationException("Sherpa worker received a command before configure.");
        }
    }

    private static (SherpaWorkerCommand Command, byte[] Payload) ReadRequest(Stream stream)
    {
        var header = ReadExact(stream, 6);
        if (header[0] != 1)
        {
            throw new InvalidOperationException($"Unsupported Sherpa worker protocol version {header[0]}.");
        }

        var command = (SherpaWorkerCommand)header[1];
        var length = BitConverter.ToInt32(header, 2);
        if (length < 0)
        {
            throw new InvalidOperationException("Sherpa worker received a negative payload length.");
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
                throw new EndOfStreamException("Sherpa worker pipe closed unexpectedly.");
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
