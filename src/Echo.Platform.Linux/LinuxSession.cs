namespace echo.Platform.Linux;

public enum LinuxSessionType
{
    Unknown,
    Wayland,
    X11,
}

public static class LinuxSession
{
    public static LinuxSessionType Type { get; } = Detect();

    public static bool IsWayland => Type == LinuxSessionType.Wayland;

    public static bool IsX11 => Type == LinuxSessionType.X11;

    public static bool IsGnome => DetectDesktopContains("gnome");

    public static bool IsKde => DetectDesktopContains("kde");

    public static bool IsWlroots => IsWayland && !IsGnome && !IsKde && DetectWlroots();

    private static bool DetectDesktopContains(string token)
    {
        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
        if (!string.IsNullOrWhiteSpace(desktop)
            && desktop.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var session = Environment.GetEnvironmentVariable("DESKTOP_SESSION");
        return !string.IsNullOrWhiteSpace(session)
            && session.Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    private static bool DetectWlroots()
    {
        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty;
        if (desktop.Contains("sway", StringComparison.OrdinalIgnoreCase)
            || desktop.Contains("hyprland", StringComparison.OrdinalIgnoreCase)
            || desktop.Contains("i3", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        return string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase)
            && !IsGnome
            && !IsKde;
    }

    private static LinuxSessionType Detect()
    {
        if (!OperatingSystem.IsLinux())
        {
            return LinuxSessionType.Unknown;
        }

        var wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        if (!string.IsNullOrWhiteSpace(wayland))
        {
            return LinuxSessionType.Wayland;
        }

        var display = Environment.GetEnvironmentVariable("DISPLAY");
        if (!string.IsNullOrWhiteSpace(display))
        {
            return LinuxSessionType.X11;
        }

        return LinuxSessionType.Unknown;
    }
}
