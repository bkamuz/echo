using System.Runtime.InteropServices;
using echo.Abstractions.Platform;

namespace echo.Platform.Windows;

public sealed class WindowsFocusTarget : IFocusTarget
{
    public nint CaptureTargetWindow() => GetForegroundWindow();

    public void RestoreTargetWindow(nint handle)
    {
        if (handle == 0 || !IsWindow(handle) || IsOwnWindow(handle))
        {
            return;
        }

        var foreground = GetForegroundWindow();
        if (foreground == handle)
        {
            // Already foreground — do not call ShowWindow/SetForegroundWindow.
            // Chromium contenteditables drop their caret on those calls even when
            // the top-level HWND does not change.
            return;
        }

        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var targetThread = GetWindowThreadProcessId(handle, out _);
        var currentThread = GetCurrentThreadId();

        if (foregroundThread != 0 && foregroundThread != currentThread)
        {
            AttachThreadInput(currentThread, foregroundThread, true);
        }

        if (targetThread != currentThread)
        {
            AttachThreadInput(currentThread, targetThread, true);
        }

        // No ShowWindow: SW_SHOW* can blur the focused DOM/input even with
        // SW_SHOWNOACTIVATE. SetForegroundWindow alone is enough to retarget
        // SendInput / Ctrl+V after an Echo overlay briefly stole activation.
        _ = SetForegroundWindow(handle);

        if (targetThread != currentThread)
        {
            AttachThreadInput(currentThread, targetThread, false);
        }

        if (foregroundThread != 0 && foregroundThread != currentThread)
        {
            AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    public bool IsOwnWindow(nint handle)
    {
        if (handle == 0 || !IsWindow(handle))
        {
            return false;
        }

        _ = GetWindowThreadProcessId(handle, out var windowProcessId);
        return windowProcessId == (uint)Environment.ProcessId;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
}
