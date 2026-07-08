using echo.Abstractions.Platform;

namespace echo.Platform.Linux;

public sealed class LinuxAudioCapture : IAudioCapture
{
    public IReadOnlyList<AudioDeviceInfo> ListInputDevices() =>
        [new AudioDeviceInfo("default", "Default microphone")];

    public void StartRecording(int sampleRate, string? deviceName = null) =>
        throw new PlatformNotSupportedException("Audio capture on Linux requires ALSA/PulseAudio implementation.");

    public float[] StopRecording() =>
        throw new PlatformNotSupportedException("Audio capture on Linux requires ALSA/PulseAudio implementation.");
}
