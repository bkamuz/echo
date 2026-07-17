namespace echo.Platform.Linux;

public enum LinuxInputAccessState
{
    Granted,
    PendingRelogin,
    NotConfigured,
}

public static class LinuxHotkeySetup
{
    public static LinuxInputAccessState GetAccessState()
    {
        if (LinuxEvdevNative.CanAccessKeyboardDevices())
        {
            return LinuxInputAccessState.Granted;
        }

        return IsListedInInputGroup()
            ? LinuxInputAccessState.PendingRelogin
            : LinuxInputAccessState.NotConfigured;
    }

    public static bool HasActiveInputGroupSession()
    {
        var user = Environment.UserName;
        if (string.IsNullOrWhiteSpace(user))
        {
            return false;
        }

        try
        {
            var output = LinuxProcessRunner.RunCommand("groups", [], CancellationToken.None, allowFailure: true);
            return output.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains("input", StringComparer.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsListedInInputGroup()
    {
        var user = Environment.UserName;
        if (string.IsNullOrWhiteSpace(user))
        {
            return false;
        }

        try
        {
            var output = LinuxProcessRunner.RunCommand("getent", ["group", "input"], CancellationToken.None, allowFailure: true);
            var members = output.Split(':');
            if (members.Length < 4)
            {
                return false;
            }

            return members[3].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(user, StringComparer.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public static bool NeedsSgBridge() =>
        IsListedInInputGroup()
        && !LinuxEvdevNative.CanAccessKeyboardDevices()
        && !HasActiveInputGroupSession();

    public static string GetSetupMessage()
    {
        if (LinuxEvdevNative.CanAccessKeyboardDevices() || LinuxHotkeyBridgeLauncher.CanLaunch())
        {
            return string.Empty;
        }

        return GetAccessState() switch
        {
            LinuxInputAccessState.Granted => string.Empty,
            LinuxInputAccessState.PendingRelogin =>
                NeedsSgBridge()
                    ? "Loc.Linux.Hotkey.PendingReloginSg"
                    : "Loc.Linux.Hotkey.PendingRelogin",
            LinuxInputAccessState.NotConfigured =>
                "Loc.Linux.Hotkey.NotConfigured",
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
}
