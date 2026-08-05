using System.Runtime.InteropServices;
using echo.Abstractions.Platform;

namespace echo.Platform.Windows;

public sealed class WindowsHotkeyService : IHotkeyService
{
    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmKeyup = 0x0101;
    private const int WmSyskeydown = 0x0104;
    private const int WmSyskeyup = 0x0105;
    private const int LlkhfInjected = 0x10;

    private readonly HashSet<int> _requiredKeys = [];
    private readonly HashSet<int> _pressedKeys = [];
    private readonly object _lock = new();
    private bool _active;
    private IntPtr _hook = IntPtr.Zero;
    private HookProc? _hookProc;
    private SynchronizationContext? _syncContext;

    public event Action? Activated;
    public event Action? Deactivated;

    public bool IsActive => _hook != IntPtr.Zero;

    public void Configure(string hotkey)
    {
        lock (_lock)
        {
            _requiredKeys.Clear();
            foreach (var token in hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                _requiredKeys.Add(MapToken(token));
            }
        }
    }

    public void Start()
    {
        // WH_KEYBOARD_LL delivers via the installing thread's message loop.
        // Capture UI SynchronizationContext on first UI Start; later Start/Stop
        // from thread-pool (e.g. after SaveConfigAsync) must marshal back.
        if (SynchronizationContext.Current is { } current)
        {
            _syncContext = current;
        }

        RunOnHookAffinity(StartCore);
    }

    public void Stop() => RunOnHookAffinity(StopCore);

    private void StartCore()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        _hookProc = HookCallback;
        _hook = SetWindowsHookEx(WhKeyboardLl, _hookProc, GetModuleHandle(null), 0);
    }

    private void StopCore()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        lock (_lock)
        {
            _pressedKeys.Clear();
            _active = false;
        }
    }

    private void RunOnHookAffinity(Action action)
    {
        var ctx = _syncContext;
        if (ctx is null || ReferenceEquals(SynchronizationContext.Current, ctx))
        {
            action();
            return;
        }

        ctx.Send(_ => action(), null);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var vkCode = Marshal.ReadInt32(lParam);
            var flags = Marshal.ReadInt32(lParam, 8);
            if ((flags & LlkhfInjected) != 0)
            {
                return CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            var isDown = wParam == (IntPtr)WmKeydown || wParam == (IntPtr)WmSyskeydown;
            var isUp = wParam == (IntPtr)WmKeyup || wParam == (IntPtr)WmSyskeyup;

            var shouldActivate = false;
            var shouldDeactivate = false;

            lock (_lock)
            {
                if (isDown)
                {
                    _pressedKeys.Add(NormalizeVk(vkCode));
                }
                else if (isUp)
                {
                    _pressedKeys.Remove(NormalizeVk(vkCode));
                }

                var satisfied = _requiredKeys.All(_pressedKeys.Contains);

                if (satisfied && !_active)
                {
                    _active = true;
                    shouldActivate = true;
                }
                else if (_active && isUp)
                {
                    _active = false;
                    _pressedKeys.Clear();
                    shouldDeactivate = true;
                }
            }

            if (shouldActivate)
            {
                Post(Activated);
            }
            else if (shouldDeactivate)
            {
                Post(Deactivated);
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

        if (_syncContext is null)
        {
            handler();
            return;
        }

        // Hook callback already runs on the UI message loop — avoid an extra Post hop.
        if (ReferenceEquals(SynchronizationContext.Current, _syncContext))
        {
            handler();
            return;
        }

        _syncContext.Post(_ => handler(), null);
    }

    private static int NormalizeVk(int vk) => vk switch
    {
        0xA2 or 0xA3 => 0x11,
        0xA0 or 0xA1 => 0x10,
        0xA4 or 0xA5 => 0x12,
        0x5C => 0x5B,
        _ => vk,
    };

    private static int MapToken(string token) => token.ToLowerInvariant() switch
    {
        "ctrl" or "control" => 0x11,
        "cmd" or "win" or "windows" => 0x5B,
        "alt" => 0x12,
        "shift" => 0x10,
        _ => token.Length == 1 ? char.ToUpperInvariant(token[0]) : 0,
    };

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
