namespace echo.Platform.Windows;

/// <summary>
/// Pure hold-to-talk chord state machine fed by low-level keyboard events.
/// Kept free of Win32 so it can be unit-tested on any OS.
/// </summary>
public sealed class HotkeyChordTracker
{
    private readonly HashSet<int> _required = [];
    private readonly HashSet<int> _pressed = [];
    private bool _active;

    public enum Transition
    {
        None,
        Activated,
        Deactivated,
    }

    public bool IsChordActive => _active;

    public IReadOnlyCollection<int> RequiredKeys => _required;

    public void Configure(string hotkey)
    {
        _required.Clear();
        foreach (var token in hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var vk = MapToken(token);
            if (vk != 0)
            {
                _required.Add(vk);
            }
        }

        Reset();
    }

    public void Reset()
    {
        _pressed.Clear();
        _active = false;
    }

    public Transition OnKey(int vkCode, bool isDown, bool isUp)
    {
        var vk = NormalizeVk(vkCode);
        if (isDown)
        {
            _pressed.Add(vk);
        }
        else if (isUp)
        {
            _pressed.Remove(vk);
        }

        if (_required.Count == 0)
        {
            return Transition.None;
        }

        if (!_active && _required.All(_pressed.Contains))
        {
            _active = true;
            return Transition.Activated;
        }

        if (_active && isUp)
        {
            _active = false;
            _pressed.Clear();
            return Transition.Deactivated;
        }

        return Transition.None;
    }

    public static int NormalizeVk(int vk) => vk switch
    {
        0xA2 or 0xA3 => 0x11,
        0xA0 or 0xA1 => 0x10,
        0xA4 or 0xA5 => 0x12,
        0x5C => 0x5B,
        _ => vk,
    };

    public static int MapToken(string token) => token.ToLowerInvariant() switch
    {
        "ctrl" or "control" => 0x11,
        "cmd" or "win" or "windows" or "meta" or "super" => 0x5B,
        "alt" or "option" => 0x12,
        "shift" => 0x10,
        _ => token.Length == 1 ? char.ToUpperInvariant(token[0]) : 0,
    };
}

/// <summary>
/// Decides when the WH_KEYBOARD_LL hook looks dead: Windows silently unhooks a low-level
/// hook whose callback exceeds LowLevelHooksTimeout, without telling the process.
/// </summary>
public static class HotkeyHookWatchdog
{
    /// <summary>Re-install even when healthy, so a silently dropped hook recovers.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    /// <summary>Keyboard input seen by the OS this long after the last hook callback = hook lost.</summary>
    public static readonly TimeSpan StallThreshold = TimeSpan.FromSeconds(3);

    public static bool IsRefreshDue(long nowMs, long installedAtMs) =>
        nowMs - installedAtMs >= (long)RefreshInterval.TotalMilliseconds;

    public static bool ShouldReinstall(
        long nowMs,
        long installedAtMs,
        long lastCallbackMs,
        long lastSystemKeyboardInputMs,
        bool chordActive)
    {
        if (chordActive)
        {
            return false;
        }

        if (IsRefreshDue(nowMs, installedAtMs))
        {
            return true;
        }

        var lastSeen = Math.Max(installedAtMs, lastCallbackMs);
        return lastSystemKeyboardInputMs > lastSeen + (long)StallThreshold.TotalMilliseconds;
    }
}
