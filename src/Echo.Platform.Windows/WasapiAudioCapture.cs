using System.Collections.Concurrent;
using echo.Abstractions.Platform;
using NAudio.Wave;

namespace echo.Platform.Windows;

public sealed class WasapiAudioCapture : IAudioCapture
{
    private WaveInEvent? _waveIn;
    private readonly ConcurrentQueue<float> _buffer = new();
    private readonly AudioLevelMeter _levelMeter = new();

    public event EventHandler<float[]>? SpectrumChanged
    {
        add => _levelMeter.SpectrumChanged += value;
        remove => _levelMeter.SpectrumChanged -= value;
    }

    public IReadOnlyList<AudioDeviceInfo> ListInputDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            devices.Add(new AudioDeviceInfo(i.ToString(), caps.ProductName));
        }

        return devices;
    }

    public void StartRecording(int sampleRate, string? deviceName = null)
    {
        StopRecording();
        _buffer.Clear();
        _levelMeter.Configure(sampleRate);

        var deviceNumber = ResolveDeviceNumber(deviceName);

        _waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(sampleRate, 16, 1),
            BufferMilliseconds = 20,
        };
        _waveIn.DataAvailable += (_, e) =>
        {
            var sampleCount = e.BytesRecorded / 2;
            Span<float> samples = sampleCount <= 512
                ? stackalloc float[sampleCount]
                : new float[sampleCount];

            for (var i = 0; i < sampleCount; i++)
            {
                var sample = BitConverter.ToInt16(e.Buffer, i * 2) / (float)short.MaxValue;
                samples[i] = sample;
                _buffer.Enqueue(sample);
            }

            _levelMeter.ReportSamples(samples);
        };
        _waveIn.StartRecording();
    }

    /// <summary>
    /// Settings store WaveIn index as Id ("0","1",...). Legacy configs may store ProductName.
    /// </summary>
    private static int ResolveDeviceNumber(string? deviceName)
    {
        var count = WaveInEvent.DeviceCount;
        if (count <= 0 || string.IsNullOrWhiteSpace(deviceName))
        {
            return 0;
        }

        if (int.TryParse(deviceName, out var index) && index >= 0 && index < count)
        {
            return index;
        }

        for (var i = 0; i < count; i++)
        {
            var name = WaveInEvent.GetCapabilities(i).ProductName;
            if (name.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        for (var i = 0; i < count; i++)
        {
            var name = WaveInEvent.GetCapabilities(i).ProductName;
            if (name.Contains(deviceName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    public float[] StopRecording()
    {
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;
        _levelMeter.Reset();
        return _buffer.ToArray();
    }
}
