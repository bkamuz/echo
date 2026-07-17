namespace echo.Abstractions.Platform;

public interface ICursorPosition
{
    /// <summary>Screen coordinates in physical pixels.</summary>
    bool TryGetPosition(out int x, out int y);
}
