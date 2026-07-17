using echo.Abstractions.Platform;

namespace echo.Platform.MacOS;

public sealed class MacOsCursorPosition : ICursorPosition
{
    public bool TryGetPosition(out int x, out int y)
    {
        x = 0;
        y = 0;
        return false;
    }
}
