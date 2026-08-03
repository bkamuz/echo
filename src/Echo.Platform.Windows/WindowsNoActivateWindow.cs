using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace echo.Platform.Windows;

/// <summary>
/// Keeps tray overlays/toasts from stealing keyboard focus from the dictation target.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsNoActivateWindow
{
    private const int GwlExstyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExAppWindow = 0x00040000;

    public static void TryApply(nint hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == 0)
        {
            return;
        }

        var exStyle = GetWindowLong(hwnd, GwlExstyle);
        exStyle |= WsExNoActivate | WsExToolWindow;
        exStyle &= ~WsExAppWindow;
        SetWindowLong(hwnd, GwlExstyle, exStyle);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    private static int GetWindowLong(nint hWnd, int nIndex) =>
        nint.Size == 8
            ? (int)GetWindowLongPtr64(hWnd, nIndex)
            : GetWindowLong32(hWnd, nIndex);

    private static void SetWindowLong(nint hWnd, int nIndex, int value)
    {
        if (nint.Size == 8)
        {
            _ = SetWindowLongPtr64(hWnd, nIndex, value);
            return;
        }

        _ = SetWindowLong32(hWnd, nIndex, value);
    }
}
