using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using echo.Abstractions.Platform;

namespace echo.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsCursorPosition : ICursorPosition
{
    public bool TryGetPosition(out int x, out int y)
    {
        if (GetCursorPos(out var point))
        {
            x = point.X;
            y = point.Y;
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point lpPoint);
}
