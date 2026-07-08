using echo.Abstractions.Platform;

namespace echo.Platform.MacOS;

public sealed class MacOsFocusTarget : IFocusTarget
{
    public nint CaptureTargetWindow() =>
        throw new PlatformNotSupportedException("Focus target on macOS requires Accessibility API implementation.");

    public void RestoreTargetWindow(nint handle) =>
        throw new PlatformNotSupportedException("Focus target on macOS requires Accessibility API implementation.");

    public bool IsOwnWindow(nint handle) => false;
}
