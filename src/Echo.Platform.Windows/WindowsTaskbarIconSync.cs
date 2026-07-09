using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using echo.Abstractions.Platform;

namespace echo.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsTaskbarIconSync : ITaskbarIconSync, IDisposable
{
    private const int WmSetIcon = 0x0078;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const int GclpHicon = -14;
    private const int GclpHiconsm = -34;
    private const int OverlaySize = 20;

    private static readonly uint TaskbarButtonCreatedMessage = RegisterWindowMessage("TaskbarButtonCreated");
    private static readonly SubclassProc SubclassCallback = SubclassWindowProc;

    private readonly object _gate = new();
    private readonly TaskbarList3 _taskbarList = new();

    private nint _windowHandle;
    private nint _smallIcon;
    private nint _bigIcon;
    private ReadOnlyMemory<byte> _pendingPng;
    private DictationOverlayState _pendingState;
    private string _pendingDescription = string.Empty;
    private bool _subclassInstalled;
    private GCHandle _subclassHandle;

    public void Attach(nint windowHandle)
    {
        if (windowHandle == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_windowHandle == windowHandle)
            {
                return;
            }

            DetachSubclass();
            ReleaseIcons();

            _windowHandle = windowHandle;
            InstallSubclass(windowHandle);

            if (_pendingPng.Length > 0)
            {
                ApplyIconCore(_pendingPng, _pendingState, _pendingDescription);
            }
        }
    }

    public void ApplyIcon(ReadOnlyMemory<byte> pngBytes, DictationOverlayState state, string description)
    {
        lock (_gate)
        {
            _pendingPng = pngBytes;
            _pendingState = state;
            _pendingDescription = description;

            if (_windowHandle == 0)
            {
                return;
            }

            ApplyIconCore(pngBytes, state, description);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            DetachSubclass();
            ReleaseIcons();
            _windowHandle = 0;
        }
    }

    private void ApplyIconCore(ReadOnlyMemory<byte> pngBytes, DictationOverlayState state, string description)
    {
        if (_windowHandle == 0 || pngBytes.IsEmpty)
        {
            return;
        }

        ReplaceIcons(pngBytes);

        SendMessage(_windowHandle, WmSetIcon, IconSmall, _smallIcon);
        SendMessage(_windowHandle, WmSetIcon, IconBig, _bigIcon);
        SetClassLongPtr(_windowHandle, GclpHiconsm, _smallIcon);
        SetClassLongPtr(_windowHandle, GclpHicon, _bigIcon);

        // Clears overlay and prompts Explorer to redraw the taskbar icon (unglommed mode).
        _taskbarList.SetOverlayIcon(_windowHandle, 0, null);

        // Pinned/combined taskbar buttons ignore WM_SETICON; overlay is the supported status channel.
        if (state != DictationOverlayState.Hidden)
        {
            _taskbarList.SetOverlayIcon(_windowHandle, _smallIcon, description);
        }
    }

    private void ReplaceIcons(ReadOnlyMemory<byte> pngBytes)
    {
        ReleaseIcons();

        using var source = new Bitmap(new MemoryStream(pngBytes.ToArray()));

        using var smallBitmap = new Bitmap(source, new Size(16, 16));
        using var bigBitmap = new Bitmap(source, new Size(32, 32));

        _smallIcon = smallBitmap.GetHicon();
        _bigIcon = bigBitmap.GetHicon();
    }

    private void ReleaseIcons()
    {
        if (_smallIcon != 0)
        {
            DestroyIcon(_smallIcon);
            _smallIcon = 0;
        }

        if (_bigIcon != 0)
        {
            DestroyIcon(_bigIcon);
            _bigIcon = 0;
        }
    }

    private void InstallSubclass(nint windowHandle)
    {
        if (_subclassInstalled)
        {
            return;
        }

        _subclassHandle = GCHandle.Alloc(this);
        if (!SetWindowSubclass(windowHandle, SubclassCallback, 1, GCHandle.ToIntPtr(_subclassHandle)))
        {
            _subclassHandle.Free();
            return;
        }

        _subclassInstalled = true;
    }

    private void DetachSubclass()
    {
        if (!_subclassInstalled || _windowHandle == 0)
        {
            return;
        }

        RemoveWindowSubclass(_windowHandle, SubclassCallback, 1);
        _subclassInstalled = false;

        if (_subclassHandle.IsAllocated)
        {
            _subclassHandle.Free();
        }
    }

    private static nint SubclassWindowProc(
        nint hWnd,
        uint uMsg,
        nint wParam,
        nint lParam,
        nuint uIdSubclass,
        nint dwRefData)
    {
        if (uMsg == TaskbarButtonCreatedMessage && GCHandle.FromIntPtr(dwRefData).Target is WindowsTaskbarIconSync sync)
        {
            lock (sync._gate)
            {
                if (sync._pendingPng.Length > 0)
                {
                    sync.ApplyIconCore(sync._pendingPng, sync._pendingState, sync._pendingDescription);
                }
            }
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint hWnd, int msg, int wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern nint SetClassLongPtr(nint hWnd, int index, nint newLong);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(
        nint hWnd,
        SubclassProc pfnSubclass,
        uint uIdSubclass,
        nint dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(
        nint hWnd,
        SubclassProc pfnSubclass,
        uint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(
        nint hWnd,
        uint uMsg,
        nint wParam,
        nint lParam,
        nuint uIdSubclass,
        nint dwRefData);

    private sealed class TaskbarList3
    {
        private readonly ITaskbarList3 _taskbarList;

        public TaskbarList3()
        {
            var type = Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11d0-958A-006097C9A090"))
                ?? throw new PlatformNotSupportedException("TaskbarList COM class is unavailable.");
            _taskbarList = (ITaskbarList3)Activator.CreateInstance(type)!;
            _taskbarList.HrInit();
        }

        public void SetOverlayIcon(nint hwnd, nint icon, string? description)
        {
            _taskbarList.SetOverlayIcon(hwnd, icon, description);
        }
    }


    [ComImport]
    [Guid("56FDF342-FD6D-11d0-958A-006097C9A090")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(nint hwnd);
        void DeleteTab(nint hwnd);
        void ActivateTab(nint hwnd);
        void SetActiveAlt(nint hwnd);
        void MarkFullscreenWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(nint hwnd, ulong completed, ulong total);
        void SetProgressState(nint hwnd, int state);
        void RegisterTab(nint hwndTab, nint hwndMDI);
        void UnregisterTab(nint hwndTab);
        void SetTabOrder(nint hwndTab, nint hwndInsertBefore);
        void SetTabActive(nint hwndTab, nint hwndMDI, uint flags);
        void SetThumbnailClip(nint hwnd, nint clip);
        void SetThumbnailTooltip(nint hwnd, [MarshalAs(UnmanagedType.LPWStr)] string tooltip);
        void SetTabProperties(nint hwnd, int stpFlags);
        void SetOverlayIcon(nint hwnd, nint icon, [MarshalAs(UnmanagedType.LPWStr)] string? description);
    }
}
