namespace echo.Core;

public enum HotkeyDisplayPlatform
{
    Windows,
    Linux,
    MacOS,
}

public static class HotkeyTokens
{
    private static readonly Dictionary<string, string> TokenMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = "Ctrl",
        ["alt"] = "Alt",
        ["shift"] = "Shift",
    };

    public static string ToDisplay(string hotkey) => ToDisplay(hotkey, ResolveCurrentPlatform());

    internal static string ToDisplay(string hotkey, HotkeyDisplayPlatform platform)
    {
        var metaLabel = GetMetaKeyDisplayName(platform);
        var tokens = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            if (IsMetaToken(tokens[i]))
            {
                tokens[i] = metaLabel;
            }
            else if (TokenMap.TryGetValue(tokens[i], out var mapped))
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

    private static HotkeyDisplayPlatform ResolveCurrentPlatform()
    {
        if (OperatingSystem.IsMacOS())
        {
            return HotkeyDisplayPlatform.MacOS;
        }

        if (OperatingSystem.IsLinux())
        {
            return HotkeyDisplayPlatform.Linux;
        }

        return HotkeyDisplayPlatform.Windows;
    }

    private static string GetMetaKeyDisplayName(HotkeyDisplayPlatform platform) => platform switch
    {
        HotkeyDisplayPlatform.MacOS => "⌘",
        HotkeyDisplayPlatform.Linux => "Super",
        HotkeyDisplayPlatform.Windows => "Win",
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null),
    };

    private static bool IsMetaToken(string token) =>
        token.Equals("cmd", StringComparison.OrdinalIgnoreCase)
        || token.Equals("win", StringComparison.OrdinalIgnoreCase);
}
