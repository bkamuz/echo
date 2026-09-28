namespace echo.Abstractions.Platform;

public interface ITrayStateService
{
    void SetState(DictationOverlayState state);

    /// <summary>
    /// Applies overlay/tray state on the UI thread and returns when it is visible to the user.
    /// </summary>
    Task SetStateAsync(DictationOverlayState state);
}
