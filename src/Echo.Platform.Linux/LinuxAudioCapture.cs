using System.Collections.Concurrent;
using System.Diagnostics;
using echo.Abstractions.Platform;

namespace echo.Platform.Linux;

public sealed class LinuxAudioCapture : IAudioCapture
{
    private readonly ConcurrentQueue<float> _buffer = new();
    private readonly AudioLevelMeter _levelMeter = new();
    private Process? _recordProcess;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;

    public event EventHandler<float[]>? SpectrumChanged
    {
        add => _levelMeter.SpectrumChanged += value;
        remove => _levelMeter.SpectrumChanged -= value;
    }

    public IReadOnlyList<AudioDeviceInfo> ListInputDevices()
    {
        if (!LinuxCommandHelper.CommandExists("arecord"))
        {
            return [new AudioDeviceInfo("default", "Default microphone")];
        }

        var devices = new List<AudioDeviceInfo>();
        try
        {
            var output = RunCommand("arecord", "-L");
            string? currentId = null;
            foreach (var rawLine in output.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                if (rawLine.TrimStart().StartsWith('#'))
                {
                    continue;
                }

                if (!char.IsWhiteSpace(rawLine[0]))
                {
                    currentId = rawLine.Trim();
                    continue;
                }

                if (currentId is null || currentId == "null")
                {
                    continue;
                }

                var label = rawLine.Trim();
                if (devices.All(d => d.Id != currentId))
                {
                    devices.Add(new AudioDeviceInfo(currentId, label));
                }

                currentId = null;
            }
        }
        catch
        {
            // Fall back to default below.
        }

        if (devices.Count == 0 || devices.All(d => d.Id != "default"))
        {
            devices.Insert(0, new AudioDeviceInfo("default", "Default microphone"));
        }

        return devices;
    }

    public void StartRecording(int sampleRate, string? deviceName = null)
    {
        StopRecording();
        _buffer.Clear();

        if (!LinuxCommandHelper.CommandExists("arecord"))
        {
            throw new PlatformNotSupportedException(
                "Audio capture on Linux requires the 'arecord' utility (alsa-utils package).");
        }

        _levelMeter.Configure(sampleRate);

        var device = ResolveDevice(deviceName);
        var startInfo = new ProcessStartInfo
        {
            FileName = "arecord",
            Arguments = FormattableString.Invariant(
                $"-f S16_LE -r {sampleRate} -c 1 -D {QuoteArg(device)} -t raw -q -"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        _recordProcess = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start arecord.");

        _readCts = new CancellationTokenSource();
        var stream = _recordProcess.StandardOutput.BaseStream;
        var token = _readCts.Token;
        _readTask = Task.Run(async () =>
        {
            var chunk = new byte[1024];
            while (!token.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), token).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                for (var offset = 0; offset + 1 < read; offset += 2)
                {
                    var sample = BitConverter.ToInt16(chunk, offset);
                    _buffer.Enqueue(sample / (float)short.MaxValue);
                }

                _levelMeter.ReportPcm16Le(chunk.AsSpan(0, read));
            }
        }, token);
    }

    public float[] StopRecording()
    {
        try
        {
            _readCts?.Cancel();
            if (_recordProcess is { HasExited: false })
            {
                _recordProcess.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort shutdown.
        }

        try
        {
            _readTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Ignore read task cancellation errors.
        }

        var samples = _buffer.ToArray();

        _readTask = null;
        _readCts?.Dispose();
        _readCts = null;
        _recordProcess?.Dispose();
        _recordProcess = null;
        _levelMeter.Reset();

        return samples;
    }

    private static string ResolveDevice(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)
            || deviceName.Equals("default", StringComparison.OrdinalIgnoreCase)
            || deviceName.Equals("Default microphone", StringComparison.OrdinalIgnoreCase))
        {
            return "default";
        }

        if (deviceName is "CARD=PCH" or "CARD=PCH,DEV=0")
        {
            return "default";
        }

        return deviceName;
    }

    private static string QuoteArg(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    private static string RunCommand(string fileName, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException($"Failed to start {fileName}.");

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }
}
