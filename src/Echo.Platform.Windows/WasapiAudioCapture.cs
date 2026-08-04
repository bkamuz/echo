using System.Collections.Concurrent;
using echo.Abstractions.Platform;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace echo.Platform.Windows;

/// <summary>
/// Shared-mode WASAPI capture (never exclusive). Replaces fragile MME WaveInEvent.
/// </summary>
public sealed class WasapiAudioCapture : IAudioCapture, IDisposable
{
    private const int StopTimeoutMs = 2000;

    private readonly object _gate = new();
    private readonly ConcurrentQueue<float> _buffer = new();
    private readonly AudioLevelMeter _levelMeter = new();
    private readonly ILogger<WasapiAudioCapture>? _logger;

    private WasapiCapture? _capture;
    private MMDevice? _device;
    private EventHandler<WaveInEventArgs>? _dataHandler;
    private WaveFormat? _captureFormat;
    private int _targetSampleRate = 16000;
    private double _resamplePhase;
    private float[] _monoScratch = [];
    private float[] _resampleScratch = [];
    private volatile bool _acceptingData;

    public WasapiAudioCapture(ILogger<WasapiAudioCapture>? logger = null)
    {
        _logger = logger;
    }

    public event EventHandler<float[]>? SpectrumChanged
    {
        add => _levelMeter.SpectrumChanged += value;
        remove => _levelMeter.SpectrumChanged -= value;
    }

    public IReadOnlyList<AudioDeviceInfo> ListInputDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(d => new AudioDeviceInfo(d.ID, d.FriendlyName))
            .ToList();
    }

    public AudioDeviceInfo? FindListedDevice(string? storedId)
    {
        var devices = ListInputDevices();
        if (devices.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(storedId))
        {
            return devices[0];
        }

        var byId = devices.FirstOrDefault(d => d.Id.Equals(storedId, StringComparison.OrdinalIgnoreCase));
        if (byId is not null)
        {
            return byId;
        }

        if (int.TryParse(storedId, out var index) && index >= 0)
        {
            var waveInName = TryGetWaveInProductName(index);
            if (!string.IsNullOrWhiteSpace(waveInName))
            {
                var match = MatchListedByName(devices, waveInName);
                if (match is not null)
                {
                    return match;
                }
            }

            if (index < devices.Count)
            {
                return devices[index];
            }
        }

        return MatchListedByName(devices, storedId) ?? devices[0];
    }

    public void StartRecording(int sampleRate, string? deviceName = null)
    {
        lock (_gate)
        {
            StopRecordingCore();
            _buffer.Clear();
            _resamplePhase = 0;
            _targetSampleRate = sampleRate > 0 ? sampleRate : 16000;
            _levelMeter.Configure(_targetSampleRate);

            _device = ResolveDevice(deviceName);
            try
            {
                var capture = new WasapiCapture(_device)
                {
                    ShareMode = AudioClientShareMode.Shared,
                };
                _captureFormat = capture.WaveFormat;
                _dataHandler = OnDataAvailable;
                capture.DataAvailable += _dataHandler;
                _capture = capture;
                _acceptingData = true;

                _logger?.LogInformation(
                    "Starting WASAPI capture device={Device} mix={Rate}Hz/{Bits}bit/{Channels}ch → {Target}Hz",
                    _device.FriendlyName,
                    _captureFormat.SampleRate,
                    _captureFormat.BitsPerSample,
                    _captureFormat.Channels,
                    _targetSampleRate);

                capture.StartRecording();
            }
            catch
            {
                _acceptingData = false;
                StopRecordingCore();
                throw;
            }
        }
    }

    public float[] StopRecording()
    {
        lock (_gate)
        {
            return StopRecordingCore();
        }
    }

    private float[] StopRecordingCore()
    {
        var capture = _capture;
        if (capture is null)
        {
            _levelMeter.Reset();
            return DrainBuffer();
        }

        _acceptingData = false;
        if (_dataHandler is not null)
        {
            capture.DataAvailable -= _dataHandler;
            _dataHandler = null;
        }

        using var stopped = new ManualResetEventSlim(false);
        void OnStopped(object? sender, StoppedEventArgs e) => stopped.Set();
        capture.RecordingStopped += OnStopped;
        try
        {
            try
            {
                capture.StopRecording();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "WasapiCapture.StopRecording failed");
                stopped.Set();
            }

            if (!stopped.Wait(StopTimeoutMs))
            {
                _logger?.LogWarning("WasapiCapture did not raise RecordingStopped within {Ms}ms", StopTimeoutMs);
            }
        }
        finally
        {
            capture.RecordingStopped -= OnStopped;
            try
            {
                capture.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "WasapiCapture.Dispose failed");
            }

            _capture = null;
            _captureFormat = null;
        }

        try
        {
            _device?.Dispose();
        }
        catch
        {
            // MMDevice dispose is best-effort.
        }

        _device = null;
        _levelMeter.Reset();
        var samples = DrainBuffer();
        _logger?.LogInformation("WASAPI capture stopped — {Samples} samples", samples.Length);
        return samples;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_acceptingData || e.BytesRecorded <= 0)
        {
            return;
        }

        var format = _captureFormat;
        if (format is null || format.BlockAlign <= 0)
        {
            return;
        }

        var frameCount = e.BytesRecorded / format.BlockAlign;
        if (frameCount <= 0)
        {
            return;
        }

        EnsureCapacity(ref _monoScratch, frameCount);
        ConvertToMonoFloat(e.Buffer.AsSpan(0, e.BytesRecorded), format, _monoScratch.AsSpan(0, frameCount));
        AppendResampled(_monoScratch.AsSpan(0, frameCount), format.SampleRate);
    }

    private void AppendResampled(ReadOnlySpan<float> mono, int captureRate)
    {
        if (captureRate <= 0)
        {
            return;
        }

        if (captureRate == _targetSampleRate)
        {
            for (var i = 0; i < mono.Length; i++)
            {
                _buffer.Enqueue(mono[i]);
            }

            _levelMeter.ReportSamples(mono);
            return;
        }

        var step = (double)captureRate / _targetSampleRate;
        var estimated = (int)(mono.Length / step) + 2;
        EnsureCapacity(ref _resampleScratch, estimated);
        var written = 0;
        var pos = _resamplePhase;
        while (pos < mono.Length)
        {
            var index = (int)pos;
            var frac = (float)(pos - index);
            float sample;
            if (index + 1 < mono.Length)
            {
                sample = mono[index] + (mono[index + 1] - mono[index]) * frac;
            }
            else
            {
                sample = mono[index];
            }

            _resampleScratch[written++] = sample;
            _buffer.Enqueue(sample);
            pos += step;
        }

        _resamplePhase = pos - mono.Length;
        if (written > 0)
        {
            _levelMeter.ReportSamples(_resampleScratch.AsSpan(0, written));
        }
    }

    private static void ConvertToMonoFloat(ReadOnlySpan<byte> bytes, WaveFormat format, Span<float> mono)
    {
        var channels = Math.Max(1, format.Channels);
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            for (var frame = 0; frame < mono.Length; frame++)
            {
                float sum = 0;
                for (var ch = 0; ch < channels; ch++)
                {
                    var offset = (frame * channels + ch) * 4;
                    sum += BitConverter.ToSingle(bytes.Slice(offset, 4));
                }

                mono[frame] = sum / channels;
            }

            return;
        }

        if (format.BitsPerSample == 16)
        {
            for (var frame = 0; frame < mono.Length; frame++)
            {
                float sum = 0;
                for (var ch = 0; ch < channels; ch++)
                {
                    var offset = (frame * channels + ch) * 2;
                    var sample = (short)(bytes[offset] | (bytes[offset + 1] << 8));
                    sum += sample / (float)short.MaxValue;
                }

                mono[frame] = sum / channels;
            }

            return;
        }

        if (format.BitsPerSample == 24)
        {
            for (var frame = 0; frame < mono.Length; frame++)
            {
                float sum = 0;
                for (var ch = 0; ch < channels; ch++)
                {
                    var offset = (frame * channels + ch) * 3;
                    var sample = bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16);
                    if ((sample & 0x800000) != 0)
                    {
                        sample |= unchecked((int)0xFF000000);
                    }

                    sum += sample / 8388608f;
                }

                mono[frame] = sum / channels;
            }

            return;
        }

        if (format.BitsPerSample == 32)
        {
            for (var frame = 0; frame < mono.Length; frame++)
            {
                float sum = 0;
                for (var ch = 0; ch < channels; ch++)
                {
                    var offset = (frame * channels + ch) * 4;
                    var sample = BitConverter.ToInt32(bytes.Slice(offset, 4));
                    sum += sample / (float)int.MaxValue;
                }

                mono[frame] = sum / channels;
            }

            return;
        }

        mono.Clear();
    }

    /// <summary>
    /// Settings may store WASAPI endpoint Id, legacy WaveIn index ("0","1",...), or ProductName.
    /// </summary>
    private MMDevice ResolveDevice(string? deviceIdOrName)
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .ToList();

        if (devices.Count == 0)
        {
            throw new InvalidOperationException("No active capture devices found.");
        }

        MMDevice PickDefault() =>
            enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

        if (string.IsNullOrWhiteSpace(deviceIdOrName))
        {
            return PickDefault();
        }

        var byId = devices.Find(d => d.ID.Equals(deviceIdOrName, StringComparison.OrdinalIgnoreCase));
        if (byId is not null)
        {
            return byId;
        }

        if (int.TryParse(deviceIdOrName, out var index) && index >= 0)
        {
            var waveInName = TryGetWaveInProductName(index);
            if (!string.IsNullOrWhiteSpace(waveInName))
            {
                var match = MatchByFriendlyName(devices, waveInName);
                if (match is not null)
                {
                    _logger?.LogInformation(
                        "Mapped legacy WaveIn index {Index} ({WaveInName}) → {WasapiName}",
                        index,
                        waveInName,
                        match.FriendlyName);
                    return match;
                }
            }

            if (index < devices.Count)
            {
                _logger?.LogInformation(
                    "Mapped legacy WaveIn index {Index} by ordinal → {WasapiName}",
                    index,
                    devices[index].FriendlyName);
                return devices[index];
            }
        }

        var byName = MatchByFriendlyName(devices, deviceIdOrName);
        if (byName is not null)
        {
            return byName;
        }

        _logger?.LogWarning("Capture device '{Device}' not found — using default", deviceIdOrName);
        return PickDefault();
    }

    private static AudioDeviceInfo? MatchListedByName(IReadOnlyList<AudioDeviceInfo> devices, string name)
    {
        var exact = devices.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var truncated = name.Length > 31 ? name[..31].TrimEnd() : name;
        var prefix = devices.FirstOrDefault(d =>
            d.Name.StartsWith(truncated, StringComparison.OrdinalIgnoreCase)
            || truncated.StartsWith(
                d.Name.Length > 31 ? d.Name[..31] : d.Name,
                StringComparison.OrdinalIgnoreCase));
        if (prefix is not null)
        {
            return prefix;
        }

        return devices.FirstOrDefault(d =>
            d.Name.Contains(name, StringComparison.OrdinalIgnoreCase)
            || name.Contains(d.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static MMDevice? MatchByFriendlyName(List<MMDevice> devices, string name)
    {
        var exact = devices.Find(d => d.FriendlyName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        // WaveIn ProductName is often truncated (~31 chars).
        var truncated = name.Length > 31 ? name[..31].TrimEnd() : name;
        var prefix = devices.Find(d =>
            d.FriendlyName.StartsWith(truncated, StringComparison.OrdinalIgnoreCase)
            || truncated.StartsWith(
                d.FriendlyName.Length > 31 ? d.FriendlyName[..31] : d.FriendlyName,
                StringComparison.OrdinalIgnoreCase));
        if (prefix is not null)
        {
            return prefix;
        }

        return devices.Find(d =>
            d.FriendlyName.Contains(name, StringComparison.OrdinalIgnoreCase)
            || name.Contains(d.FriendlyName, StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryGetWaveInProductName(int index)
    {
        try
        {
            if (index >= 0 && index < WaveInEvent.DeviceCount)
            {
                return WaveInEvent.GetCapabilities(index).ProductName;
            }
        }
        catch
        {
            // WaveIn enumeration can fail on some drivers; fall back to ordinal WASAPI map.
        }

        return null;
    }

    private float[] DrainBuffer()
    {
        var samples = _buffer.ToArray();
        while (_buffer.TryDequeue(out _))
        {
        }

        return samples;
    }

    private static void EnsureCapacity(ref float[] buffer, int needed)
    {
        if (buffer.Length < needed)
        {
            buffer = new float[needed];
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            StopRecordingCore();
        }
    }
}
