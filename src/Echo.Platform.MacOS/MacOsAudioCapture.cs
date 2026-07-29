using echo.Abstractions.Platform;

namespace echo.Platform.MacOS;

public sealed class MacOsAudioCapture : IAudioCapture
{
    // Stub until AVFoundation capture exists — keep the interface event without unused-field warning.
    public event EventHandler<float[]>? SpectrumChanged
    {
        add { }
        remove { }
    }

    public IReadOnlyList<AudioDeviceInfo> ListInputDevices() =>
        [new AudioDeviceInfo("default", "Default microphone")];

    public void StartRecording(int sampleRate, string? deviceName = null) =>
        throw new PlatformNotSupportedException("Audio capture on macOS requires AVFoundation implementation.");

    public float[] StopRecording() =>
        throw new PlatformNotSupportedException("Audio capture on macOS requires AVFoundation implementation.");
}
