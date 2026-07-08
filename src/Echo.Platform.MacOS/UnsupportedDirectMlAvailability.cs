using echo.Abstractions.Platform;

namespace echo.Platform.MacOS;

public sealed class UnsupportedDirectMlAvailability : IDirectMlAvailability
{
    public bool IsAvailable => false;
}
