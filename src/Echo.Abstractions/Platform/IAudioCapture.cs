namespace echo.Abstractions.Platform;

public sealed record AudioDeviceInfo(string Id, string Name);

public interface IAudioCapture
{
    /// <summary>Raised with band levels 0..1 while recording (throttled ~16 ms).</summary>
    event EventHandler<float[]>? SpectrumChanged;

    IReadOnlyList<AudioDeviceInfo> ListInputDevices();
    void StartRecording(int sampleRate, string? deviceName = null);
    float[] StopRecording();
}
