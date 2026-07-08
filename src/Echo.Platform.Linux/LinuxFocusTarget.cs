using echo.Abstractions.Platform;

namespace echo.Platform.Linux;

public sealed class LinuxFocusTarget : IFocusTarget
{
    public nint CaptureTargetWindow() =>
        throw new PlatformNotSupportedException("Focus target on Linux requires X11/Wayland implementation.");

    public void RestoreTargetWindow(nint handle) =>
        throw new PlatformNotSupportedException("Focus target on Linux requires X11/Wayland implementation.");

    public bool IsOwnWindow(nint handle) => false;
}
