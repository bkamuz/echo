using Avalonia.Input;
using echo.Core;

namespace echo.App;

public sealed class HotkeyCaptureSession
{
    private static readonly string[] TokenOrder = ["ctrl", "cmd", "alt", "shift"];

    private readonly HashSet<Key> _pressedKeys = [];
    private readonly HashSet<string> _tokens = [];

    public void Reset()
    {
        _pressedKeys.Clear();
        _tokens.Clear();
    }

    public string Preview => HotkeyTokens.ToDisplay(BuildHotkey());

    public void RegisterKeyDown(KeyEventArgs e)
    {
        _pressedKeys.Add(e.Key);
        AddTokens(e.Key, e.KeyModifiers);
    }

    public bool TryCompleteOnKeyUp(KeyEventArgs e, out string hotkey)
    {
        _pressedKeys.Remove(e.Key);

        if (_pressedKeys.Count > 0)
        {
            hotkey = string.Empty;
            return false;
        }

        hotkey = BuildHotkey();
        var valid = _tokens.Count > 0 && HotkeyTokens.IsValid(hotkey);
        Reset();
        return valid;
    }

    private void AddTokens(Key key, KeyModifiers modifiers)
    {
        AddModifierTokenFromKey(key);

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            _tokens.Add("ctrl");
        }

        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            _tokens.Add("cmd");
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            _tokens.Add("alt");
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            _tokens.Add("shift");
        }

        if (!IsModifierKey(key) && TryMapKeyToken(key, out var keyToken))
        {
            _tokens.Add(keyToken);
        }
    }

    private void AddModifierTokenFromKey(Key key)
    {
        var token = key switch
        {
            Key.LeftCtrl or Key.RightCtrl => "ctrl",
            Key.LWin or Key.RWin => "cmd",
            Key.LeftAlt or Key.RightAlt => "alt",
            Key.LeftShift or Key.RightShift => "shift",
            _ => null
        };

        if (token is not null)
        {
            _tokens.Add(token);
        }
    }

    private string BuildHotkey()
    {
        if (_tokens.Count == 0)
        {
            return string.Empty;
        }

        var ordered = new List<string>();
        foreach (var token in TokenOrder)
        {
            if (_tokens.Contains(token))
            {
                ordered.Add(token);
            }
        }

        foreach (var token in _tokens.OrderBy(t => t, StringComparer.Ordinal))
        {
            if (!TokenOrder.Contains(token))
            {
                ordered.Add(token);
            }
        }

        return string.Join('+', ordered);
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;

    private static bool TryMapKeyToken(Key key, out string token)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            token = ((char)('a' + (key - Key.A))).ToString();
            return true;
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            token = ((char)('0' + (key - Key.D0))).ToString();
            return true;
        }

        if (key is >= Key.F1 and <= Key.F12)
        {
            token = $"f{(int)(key - Key.F1) + 1}";
            return true;
        }

        token = string.Empty;
        return false;
    }
}
