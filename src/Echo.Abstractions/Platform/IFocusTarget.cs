namespace echo.Abstractions.Platform;

public interface IFocusTarget
{
    nint CaptureTargetWindow();

    /// <summary>
    /// Captures the keyboard-focus HWND within the foreground thread (0 when unknown).
    /// </summary>
    nint CaptureTargetFocus();

    void RestoreTargetWindow(nint handle);

    /// <summary>
    /// Restores keyboard focus to a previously captured focus HWND.
    /// </summary>
    void RestoreTargetFocus(nint focusHandle);

    bool IsOwnWindow(nint handle);
}
