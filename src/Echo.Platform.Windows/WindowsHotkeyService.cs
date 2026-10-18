using System.Runtime.InteropServices;
using echo.Abstractions.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace echo.Platform.Windows;

/// <summary>
/// Global hold-to-talk hotkey via WH_KEYBOARD_LL.
/// The hook lives on a dedicated thread with its own message pump (not the Avalonia UI thread):
/// a busy/throttled UI thread made the callback exceed LowLevelHooksTimeout, after which Windows
/// silently removes the hook — the hotkey then only "worked" while Echo was focused.
/// A watchdog re-installs the hook if it stops seeing keyboard input the OS did see.
/// </summary>
public sealed class WindowsHotkeyService : IHotkeyService, IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmKeyup = 0x0101;
    private const int WmSyskeydown = 0x0104;
    private const int WmSyskeyup = 0x0105;
    private const uint WmQuit = 0x0012;
    private const uint WmApp = 0x8000;
    private const uint WmReinstall = WmApp + 1;
    private const int LlkhfInjected = 0x10;

    private readonly ILogger _logger;
    private readonly HotkeyChordTracker _tracker = new();
    private readonly object _lock = new();
    private readonly HookProc _hookProc;
    private SynchronizationContext? _syncContext;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook = IntPtr.Zero;
    private volatile bool _enabled;
    private long _installedAtMs;
    private long _lastCallbackMs;
    private long _lastKeyCallbackMs;
    private long _callbackCount;
    private int _reinstallCount;
    private Timer? _watchdog;
    private string _hotkey = string.Empty;

    public WindowsHotkeyService()
        : this(NullLogger<WindowsHotkeyService>.Instance)
    {
    }

    public WindowsHotkeyService(ILogger<WindowsHotkeyService> logger)
    {
        _logger = logger;
        _hookProc = HookCallback;
    }

    public event Action? Activated;
    public event Action? Deactivated;

    public bool IsActive => _enabled && _hook != IntPtr.Zero;

    public void Configure(string hotkey)
    {
        lock (_lock)
        {
            _hotkey = hotkey;
            _tracker.Configure(hotkey);
        }

        _logger.LogInformation(
            "Hotkey configured: {Hotkey} -> VK [{Keys}]",
            hotkey,
            string.Join(",", _tracker.RequiredKeys.Select(k => $"0x{k:X2}")));
    }

    public void Start()
    {
        if (SynchronizationContext.Current is { } current)
        {
            _syncContext = current;
        }

        EnsureHookThread();
        lock (_lock)
        {
            _tracker.Reset();
        }

        _enabled = true;
        _logger.LogInformation(
            "Hotkey listening: {Hotkey} (hook=0x{Hook:X}, thread={Thread}, callbacks={Count})",
            _hotkey,
            _hook.ToInt64(),
            _threadId,
            Interlocked.Read(ref _callbackCount));
    }

    public void Stop()
    {
        _enabled = false;
        lock (_lock)
        {
            _tracker.Reset();
        }

        _logger.LogInformation("Hotkey muted (hook kept on dedicated thread)");
    }

    public void Dispose()
    {
        _enabled = false;
        _watchdog?.Dispose();
        _watchdog = null;
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }

        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _threadId = 0;
    }

    private void EnsureHookThread()
    {
        if (_thread is not null)
        {
            return;
        }

        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => HookThreadMain(ready))
        {
            IsBackground = true,
            Name = "Echo hotkey hook",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(5)))
        {
            _logger.LogWarning("Hotkey hook thread did not report ready within 5 s");
        }

        _watchdog = new Timer(_ => WatchdogTick(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    private void HookThreadMain(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        // Force creation of this thread's message queue before anyone posts to it.
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        InstallHook("initial");
        ready.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WmReinstall)
            {
                InstallHook(msg.wParam == (IntPtr)1 ? "watchdog: stalled" : "watchdog: periodic refresh");
                continue;
            }

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        UninstallHook();
        _logger.LogInformation("Hotkey hook thread exited");
    }

    private void InstallHook(string reason)
    {
        UninstallHook();
        _hook = SetWindowsHookEx(WhKeyboardLl, _hookProc, GetModuleHandle(null), 0);
        var err = _hook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
        Interlocked.Exchange(ref _installedAtMs, Environment.TickCount64);
        lock (_lock)
        {
            _tracker.Reset();
        }

        if (_hook == IntPtr.Zero)
        {
            _logger.LogError("SetWindowsHookEx(WH_KEYBOARD_LL) failed ({Reason}), Win32 error {Error}", reason, err);
        }
        else if (reason == "initial" || reason.Contains("stalled", StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Keyboard hook installed ({Reason}) hook=0x{Hook:X} thread={Thread} reinstalls={Count}",
                reason,
                _hook.ToInt64(),
                _threadId,
                _reinstallCount);
        }
    }

    private void UninstallHook()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private void WatchdogTick()
    {
        try
        {
            if (_threadId == 0)
            {
                return;
            }

            var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (!GetLastInputInfo(ref info))
            {
                return;
            }

            var now = Environment.TickCount64;
            // GetLastInputInfo is a 32-bit tick; map it onto TickCount64.
            var lastInput = now - unchecked((uint)Environment.TickCount - info.dwTime);
            bool chordActive;
            lock (_lock)
            {
                chordActive = _tracker.IsChordActive;
            }

            // GetLastInputInfo also counts mouse input, so a stall only counts while a key is
            // physically held and the hook has not seen any keyboard event for a while.
            var installedAt = Interlocked.Read(ref _installedAtMs);
            var lastKey = Interlocked.Read(ref _lastKeyCallbackMs);
            if (!HotkeyHookWatchdog.ShouldReinstall(now, installedAt, lastKey, lastInput, chordActive))
            {
                return;
            }

            var periodic = HotkeyHookWatchdog.IsRefreshDue(now, installedAt);
            var stalled = !periodic && IsAnyKeyboardKeyDown();
            if (!periodic && !stalled)
            {
                return;
            }

            if (stalled || periodic)
            {
                if (stalled)
                {
                    _logger.LogWarning(
                        "Keyboard hook stalled (last key callback {Ago} ms ago, OS input {InputAgo} ms ago) — reinstalling",
                        now - Interlocked.Read(ref _lastKeyCallbackMs),
                        now - lastInput);
                }

                Interlocked.Increment(ref _reinstallCount);
                PostThreadMessage(_threadId, WmReinstall, stalled ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Hotkey watchdog tick failed");
        }
    }

    private static bool IsAnyKeyboardKeyDown()
    {
        // Modifiers + letters/digits are enough to detect "user is typing but hook sees nothing".
        for (var vk = 0x08; vk <= 0xFE; vk++)
        {
            if ((GetAsyncKeyState(vk) & 0x8000) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var now = Environment.TickCount64;
            Interlocked.Exchange(ref _lastCallbackMs, now);
            Interlocked.Exchange(ref _lastKeyCallbackMs, now);
            Interlocked.Increment(ref _callbackCount);

            var vkCode = Marshal.ReadInt32(lParam);
            var flags = Marshal.ReadInt32(lParam, 8);
            if ((flags & LlkhfInjected) == 0 && _enabled)
            {
                var isDown = wParam == (IntPtr)WmKeydown || wParam == (IntPtr)WmSyskeydown;
                var isUp = wParam == (IntPtr)WmKeyup || wParam == (IntPtr)WmSyskeyup;
                HotkeyChordTracker.Transition transition;
                lock (_lock)
                {
                    transition = _tracker.OnKey(vkCode, isDown, isUp);
                }

                if (transition == HotkeyChordTracker.Transition.Activated)
                {
                    _logger.LogInformation("Hotkey down: {Hotkey}", _hotkey);
                    Post(Activated);
                }
                else if (transition == HotkeyChordTracker.Transition.Deactivated)
                {
                    _logger.LogInformation("Hotkey up: {Hotkey}", _hotkey);
                    Post(Deactivated);
                }
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void Post(Action? handler)
    {
        if (handler is null)
        {
            return;
        }

        // Never run app code on the hook thread: the callback must return fast.
        var ctx = _syncContext;
        if (ctx is null)
        {
            ThreadPool.QueueUserWorkItem(_ => handler());
            return;
        }

        ctx.Post(_ => handler(), null);
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
