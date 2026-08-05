using echo.Abstractions.Platform;

namespace echo.Platform.Linux;

public sealed class LinuxFocusTarget : IFocusTarget
{
    public nint CaptureTargetWindow()
    {
        if (!LinuxSession.IsX11 || !LinuxCommandHelper.CommandExists("xdotool"))
        {
            return 0;
        }

        try
        {
            var output = LinuxProcessRunner.RunCommand(
                "xdotool",
                ["getactivewindow"],
                CancellationToken.None,
                allowFailure: true);

            if (long.TryParse(output.Trim(), out var windowId) && windowId > 0)
            {
                return (nint)windowId;
            }
        }
        catch
        {
        }

        return 0;
    }

    public nint CaptureTargetFocus() => 0;

    public void RestoreTargetWindow(nint handle)
    {
        if (handle == 0 || !LinuxSession.IsX11 || !LinuxCommandHelper.CommandExists("xdotool"))
        {
            return;
        }

        try
        {
            LinuxProcessRunner.RunCommand(
                "xdotool",
                ["windowactivate", "--sync", handle.ToString()],
                CancellationToken.None,
                allowFailure: true);
        }
        catch
        {
        }
    }

    public void RestoreTargetFocus(nint focusHandle)
    {
    }

    public bool IsOwnWindow(nint handle) => false;
}
