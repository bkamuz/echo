using echo.Abstractions.Platform;

namespace echo.Platform.Linux;

public sealed class LinuxHotkeyService : IHotkeyService
{
#pragma warning disable CS0067
    public event Action? Activated;
    public event Action? Deactivated;
#pragma warning restore CS0067

    public void Configure(string hotkey) { }

    public void Start()
    {
        // Global hotkeys are not implemented on Linux yet; keep the app usable.
    }

    public void Stop() { }
}
