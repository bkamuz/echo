using echo.Engines.ParakeetNpu;
using echo.Engines.ParakeetNpu.Parakeet;
using Microsoft.Extensions.Logging.Abstractions;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Echo NPU self-test requires Windows.");
    return 1;
}

var wavPath = args.Length > 0 ? args[0] : null;
if (wavPath is null || !File.Exists(wavPath))
{
    Console.WriteLine("Usage: Echo.NpuSelfTest <path-to-16khz-mono-wav>");
    Console.WriteLine();
    Console.WriteLine("Downloads QNN runtime + Parakeet HTP model on first run, then transcribes.");
    Console.WriteLine("Requires Snapdragon X/X2 Elite (Hexagon V73/V81). Check echo.log-style output below.");
    return 1;
}

try
{
    if (SnapdragonHardware.IsSnapdragonXElite(out var processor))
    {
        Console.WriteLine($"Processor: {processor}");
    }
    else
    {
        Console.Error.WriteLine(
            $"WARNING: processor '{processor}' is not a recognized Snapdragon X/X2 Elite family name; attempting HTP anyway.");
    }

    SnapdragonHardware.LogHtpCompatibilityWarning(null);

    var runtimeDownloader = new QnnRuntimeDownloader(new HttpClient(), NullLogger<QnnRuntimeDownloader>.Instance);
    var modelDownloader = new ParakeetModelDownloader(new HttpClient(), NullLogger<ParakeetModelDownloader>.Instance);

    Console.WriteLine("Ensuring QNN runtime...");
    await runtimeDownloader.EnsureInstalledAsync(new Progress<string>(Console.WriteLine)).ConfigureAwait(false);
    Console.WriteLine("Ensuring Parakeet NPU model...");
    await modelDownloader.EnsureInstalledAsync(new Progress<string>(Console.WriteLine)).ConfigureAwait(false);

    Console.WriteLine("Loading Parakeet NPU pipeline (ORT QNN / HTP)...");
    using var pipeline = ParakeetPipeline.LoadNpu(
        ParakeetModelDownloader.ModelDir,
        QnnRuntimeDownloader.RuntimeDir,
        NullLogger<ParakeetPipeline>.Instance);

    var (samples, sampleRate) = ReadMonoWav(wavPath);
    Console.WriteLine($"Transcribing {samples.Length} samples @ {sampleRate} Hz...");
    var transcript = pipeline.Transcribe(samples, sampleRate, NullLogger<ParakeetPipeline>.Instance);

    Console.WriteLine();
    Console.WriteLine("=== RESULT ===");
    Console.WriteLine($"provider: qnn/htp");
    Console.WriteLine($"transcript: {transcript}");
    return string.IsNullOrWhiteSpace(transcript) ? 2 : 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAILED: {ex.Message}");
    Console.Error.WriteLine(ex);
    return 1;
}

static (float[] Samples, int SampleRate) ReadMonoWav(string path)
{
    using var reader = new BinaryReader(File.OpenRead(path));
    if (new string(reader.ReadChars(4)) != "RIFF")
    {
        throw new InvalidOperationException("Not a RIFF WAV file.");
    }

    reader.ReadInt32();
    if (new string(reader.ReadChars(4)) != "WAVE")
    {
        throw new InvalidOperationException("Not a WAVE file.");
    }

    short channels = 1;
    var sampleRate = 16000;
    short bitsPerSample = 16;

    while (reader.BaseStream.Position < reader.BaseStream.Length)
    {
        var chunkId = new string(reader.ReadChars(4));
        var chunkSize = reader.ReadInt32();
        if (chunkId == "fmt ")
        {
            reader.ReadInt16();
            channels = reader.ReadInt16();
            sampleRate = reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadInt16();
            bitsPerSample = reader.ReadInt16();
            if (chunkSize > 16)
            {
                reader.BaseStream.Seek(chunkSize - 16, SeekOrigin.Current);
            }
        }
        else if (chunkId == "data")
        {
            if (bitsPerSample != 16)
            {
                throw new NotSupportedException("Only 16-bit PCM WAV is supported.");
            }

            var bytes = reader.ReadBytes(chunkSize);
            var sampleCount = bytes.Length / 2 / channels;
            var samples = new float[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                var left = BitConverter.ToInt16(bytes, i * 2 * channels);
                samples[i] = left / 32768f;
            }

            return (samples, sampleRate);
        }
        else
        {
            reader.BaseStream.Seek(chunkSize, SeekOrigin.Current);
        }
    }

    throw new InvalidOperationException("WAV data chunk not found.");
}
