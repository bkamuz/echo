namespace echo.Abstractions.Platform;

public interface IFocusTarget
{
    nint CaptureTargetWindow();

    void RestoreTargetWindow(nint handle);

    bool IsOwnWindow(nint handle);
}
