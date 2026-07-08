using echo.Abstractions.Platform;

namespace echo.Platform.MacOS;

public sealed class MacOsAudioCapture : IAudioCapture
{
    public IReadOnlyList<AudioDeviceInfo> ListInputDevices() =>
        [new AudioDeviceInfo("default", "Default microphone")];

    public void StartRecording(int sampleRate, string? deviceName = null) =>
        throw new PlatformNotSupportedException("Audio capture on macOS requires AVFoundation implementation.");

    public float[] StopRecording() =>
        throw new PlatformNotSupportedException("Audio capture on macOS requires AVFoundation implementation.");
}
