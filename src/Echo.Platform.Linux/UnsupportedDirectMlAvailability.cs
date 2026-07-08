using echo.Abstractions.Platform;

namespace echo.Platform.Linux;

public sealed class UnsupportedDirectMlAvailability : IDirectMlAvailability
{
    public bool IsAvailable => false;
}
