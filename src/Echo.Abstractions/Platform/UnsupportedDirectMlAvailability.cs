using echo.Abstractions.Platform;

namespace echo.Abstractions.Platform;

/// <summary>
/// Shared stub for platforms without DirectML (Linux / macOS).
/// </summary>
public sealed class UnsupportedDirectMlAvailability : IDirectMlAvailability
{
    public bool IsAvailable => false;
}
