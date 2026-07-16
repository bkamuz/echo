using echo.Abstractions.Platform;

namespace echo.Platform.Windows;

public sealed class WindowsDirectMlAvailability : IDirectMlAvailability
{
    /// <summary>
    /// GPU option is offered on all Windows builds; natives may be downloaded on first use.
    /// </summary>
    public bool IsAvailable => OperatingSystem.IsWindows();
}
