using echo.Abstractions.Platform;

namespace echo.Platform.MacOS;

public sealed class MacOsHotkeyService : IHotkeyService
{
#pragma warning disable CS0067
    public event Action? Activated;
    public event Action? Deactivated;

    public bool IsActive => false;

    public void Configure(string hotkey) { }

    public void Start() =>
        throw new PlatformNotSupportedException("Hotkey service on macOS requires CGEventTap implementation.");

    public void Stop() { }
#pragma warning restore CS0067
}
