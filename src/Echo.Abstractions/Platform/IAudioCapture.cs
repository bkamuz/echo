namespace echo.Abstractions.Platform;

public sealed record AudioDeviceInfo(string Id, string Name);

public interface IAudioCapture
{
    IReadOnlyList<AudioDeviceInfo> ListInputDevices();
    void StartRecording(int sampleRate, string? deviceName = null);
    float[] StopRecording();
}
