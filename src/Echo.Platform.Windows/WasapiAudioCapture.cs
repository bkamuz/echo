using System.Collections.Concurrent;
using echo.Abstractions.Platform;
using NAudio.Wave;

namespace echo.Platform.Windows;

public sealed class WasapiAudioCapture : IAudioCapture
{
    private WaveInEvent? _waveIn;
    private readonly ConcurrentQueue<float> _buffer = new();

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

        var deviceNumber = 0;
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            for (var i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                if (WaveInEvent.GetCapabilities(i).ProductName.Contains(deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    deviceNumber = i;
                    break;
                }
            }
        }

        _waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(sampleRate, 16, 1),
            BufferMilliseconds = 50,
        };
        _waveIn.DataAvailable += (_, e) =>
        {
            for (var offset = 0; offset < e.BytesRecorded; offset += 2)
            {
                var sample = BitConverter.ToInt16(e.Buffer, offset);
                _buffer.Enqueue(sample / (float)short.MaxValue);
            }
        };
        _waveIn.StartRecording();
    }

    public float[] StopRecording()
    {
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;
        return _buffer.ToArray();
    }
}
