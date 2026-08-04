namespace echo.Abstractions.Platform;

public sealed record AudioDeviceInfo(string Id, string Name);

public interface IAudioCapture
{
    /// <summary>Raised with band levels 0..1 while recording (throttled ~16 ms).</summary>
    event EventHandler<float[]>? SpectrumChanged;

    IReadOnlyList<AudioDeviceInfo> ListInputDevices();

    /// <summary>
    /// Maps a stored config value (endpoint id, friendly name, or legacy WaveIn index) to a listed device.
    /// </summary>
    AudioDeviceInfo? FindListedDevice(string? storedId)
    {
        var devices = ListInputDevices();
        if (devices.Count == 0 || string.IsNullOrWhiteSpace(storedId))
        {
            return devices.FirstOrDefault();
        }

        return devices.FirstOrDefault(d => d.Id == storedId)
            ?? devices.FirstOrDefault(d => d.Name.Equals(storedId, StringComparison.OrdinalIgnoreCase))
            ?? devices.FirstOrDefault(d => d.Name.Contains(storedId, StringComparison.OrdinalIgnoreCase))
            ?? devices.FirstOrDefault();
    }

    void StartRecording(int sampleRate, string? deviceName = null);
    float[] StopRecording();
}
