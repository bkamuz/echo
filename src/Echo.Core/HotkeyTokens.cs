namespace echo.Core;

public static class HotkeyTokens
{
    private static readonly Dictionary<string, string> TokenMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = "Ctrl",
        ["cmd"] = "Win",
        ["win"] = "Win",
        ["alt"] = "Alt",
        ["shift"] = "Shift",
    };

    public static string ToDisplay(string hotkey)
    {
        var tokens = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            if (TokenMap.TryGetValue(tokens[i], out var mapped))
            {
                tokens[i] = mapped;
            }
        }

        return string.Join(" + ", tokens);
    }

    public static bool IsValid(string hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return false;
        }

        var tokens = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length > 0;
    }
}
