using echo.Platform.Linux.Injection;

namespace echo.Platform.Linux;

public sealed record LinuxDependency(
    string Id,
    string DisplayName,
    string Description,
    string Command,
    IReadOnlyDictionary<LinuxPackageManager, string> Packages)
{
    public string? GetPackageName(LinuxPackageManager packageManager) =>
        Packages.TryGetValue(packageManager, out var package) ? package : null;
}

public static class LinuxDependencyCatalog
{
    /// <summary>
    /// AT-SPI packages are not used on GNOME Wayland (Mutter blocks AT-SPI focus).
    /// </summary>
    public static bool RequiresAtSpiPackages => !UsesGnomeWaylandYdotool;

    public static bool UsesGnomeWaylandYdotool =>
        LinuxSession.IsWayland && LinuxSession.IsGnome;

    private static readonly Dictionary<LinuxPackageManager, string> AlsaPackages = new()
    {
        [LinuxPackageManager.Apt] = "alsa-utils",
        [LinuxPackageManager.Dnf] = "alsa-utils",
        [LinuxPackageManager.Pacman] = "alsa-utils",
    };

    private static readonly Dictionary<LinuxPackageManager, string> WlClipboardPackages = new()
    {
        [LinuxPackageManager.Apt] = "wl-clipboard",
        [LinuxPackageManager.Dnf] = "wl-clipboard",
        [LinuxPackageManager.Pacman] = "wl-clipboard",
    };

    private static readonly Dictionary<LinuxPackageManager, string> WtypePackages = new()
    {
        [LinuxPackageManager.Apt] = "wtype",
        [LinuxPackageManager.Dnf] = "wtype",
        [LinuxPackageManager.Pacman] = "wtype",
    };

    private static readonly Dictionary<LinuxPackageManager, string> YdotoolPackages = new()
    {
        [LinuxPackageManager.Apt] = "ydotool",
        [LinuxPackageManager.Dnf] = "ydotool",
        [LinuxPackageManager.Pacman] = "ydotool",
    };

    private static readonly Dictionary<LinuxPackageManager, string> XdotoolPackages = new()
    {
        [LinuxPackageManager.Apt] = "xdotool",
        [LinuxPackageManager.Dnf] = "xdotool",
        [LinuxPackageManager.Pacman] = "xdotool",
    };

    private static readonly Dictionary<LinuxPackageManager, string> XclipPackages = new()
    {
        [LinuxPackageManager.Apt] = "xclip",
        [LinuxPackageManager.Dnf] = "xclip",
        [LinuxPackageManager.Pacman] = "xclip",
    };

    private static readonly Dictionary<LinuxPackageManager, string> GpastePackages = new()
    {
        [LinuxPackageManager.Apt] = "gpaste-2",
        [LinuxPackageManager.Dnf] = "gpaste",
        [LinuxPackageManager.Pacman] = "gpaste",
    };

    private static readonly Dictionary<LinuxPackageManager, string> PythonGiPackages = new()
    {
        [LinuxPackageManager.Apt] = "python3-gi",
        [LinuxPackageManager.Dnf] = "python3-gobject",
        [LinuxPackageManager.Pacman] = "python-gobject",
    };

    private static readonly Dictionary<LinuxPackageManager, string> AtspiPackages = new()
    {
        [LinuxPackageManager.Apt] = "gir1.2-atspi-2.0",
        [LinuxPackageManager.Dnf] = "at-spi2-core",
        [LinuxPackageManager.Pacman] = "at-spi2-core",
    };

    private static readonly Dictionary<LinuxPackageManager, string> SgPackages = new()
    {
        [LinuxPackageManager.Apt] = "util-linux-extra",
        [LinuxPackageManager.Dnf] = "util-linux",
        [LinuxPackageManager.Pacman] = "util-linux",
    };

    public static IReadOnlyList<LinuxDependency> ForCurrentSession()
    {
        if (!OperatingSystem.IsLinux() || LinuxSession.Type == LinuxSessionType.Unknown)
        {
            return [];
        }

        var dependencies = new List<LinuxDependency>
        {
            new(
                "arecord",
                "Loc.Linux.Dep.arecord.Title",
                "Loc.Linux.Dep.arecord.Description",
                "arecord",
                AlsaPackages),
        };

        if (RequiresAtSpiPackages)
        {
            dependencies.Add(new(
                "python3-gi",
                "Loc.Linux.Dep.python3-gi.Title",
                "Loc.Linux.Dep.python3-gi.Description",
                "python3",
                PythonGiPackages));
            dependencies.Add(new(
                "atspi",
                "Loc.Linux.Dep.atspi.Title",
                "Loc.Linux.Dep.atspi.Description",
                "python3",
                AtspiPackages));
        }

        if (LinuxSession.IsWayland)
        {
            dependencies.Add(new(
                "wl-copy",
                "Loc.Linux.Dep.wl-copy.Title",
                "Loc.Linux.Dep.wl-copy.Description",
                "wl-copy",
                WlClipboardPackages));
            if (LinuxSession.IsGnome)
            {
                dependencies.Add(new(
                    "ydotool",
                    "Loc.Linux.Dep.ydotool.Title",
                    "Loc.Linux.Dep.ydotool.Description",
                    "ydotool",
                    YdotoolPackages));
                dependencies.Add(new(
                    "gpaste-client",
                    "Loc.Linux.Dep.gpaste-client.Title",
                    "Loc.Linux.Dep.gpaste-client.Description",
                    "gpaste-client",
                    GpastePackages));
            }
            else
            {
                dependencies.Add(new(
                    "wtype",
                    "Loc.Linux.Dep.wtype.Title",
                    "Loc.Linux.Dep.wtype.Description",
                    "wtype",
                    WtypePackages));
            }
        }

        if (LinuxSession.IsX11)
        {
            dependencies.Add(new(
                "xclip",
                "Loc.Linux.Dep.xclip.Title",
                "Loc.Linux.Dep.xclip.Description",
                "xclip",
                XclipPackages));
            dependencies.Add(new(
                "xdotool",
                "Loc.Linux.Dep.xdotool.Title",
                "Loc.Linux.Dep.xdotool.Description",
                "xdotool",
                XdotoolPackages));
        }

        dependencies.Add(new(
            "sg",
            "Loc.Linux.Dep.sg.Title",
            "Loc.Linux.Dep.sg.Description",
            "sg",
            SgPackages));

        return dependencies;
    }

    public static IReadOnlyList<LinuxDependency> GetMissing()
    {
        if (!OperatingSystem.IsLinux() || LinuxSession.Type == LinuxSessionType.Unknown)
        {
            return [];
        }

        var catalog = ForCurrentSession();
        var required = catalog
            .Where(dependency => dependency.Id is not ("wtype" or "ydotool" or "sg" or "gpaste-client"))
            .Where(dependency => !IsDependencySatisfied(dependency))
            .ToList();

        if (LinuxHotkeySetup.NeedsSgBridge() && !LinuxCommandHelper.CommandExists("sg"))
        {
            required.Add(catalog.First(dependency => dependency.Id == "sg"));
        }

        if (LinuxSession.IsWayland
            && LinuxSession.IsWlroots
            && !LinuxCommandHelper.CommandExists("wtype"))
        {
            required.Add(catalog.First(dependency => dependency.Id == "wtype"));
        }

        if (UsesGnomeWaylandYdotool)
        {
            var ydotool = catalog.FirstOrDefault(dependency => dependency.Id == "ydotool");
            if (ydotool is not null && !IsDependencySatisfied(ydotool))
            {
                required.Add(ydotool);
            }
        }

        return required;
    }

    private static bool IsDependencySatisfied(LinuxDependency dependency) =>
        dependency.Id switch
        {
            "python3-gi" or "atspi" =>
                !RequiresAtSpiPackages || LinuxAtSpiInserter.IsAvailable,
            "ydotool" => IsYdotoolSatisfied(),
            "wl-copy" => LinuxClipboard.IsAvailable,
            "gpaste-client" => LinuxCommandHelper.CommandExists("gpaste-client"),
            "xclip" => LinuxCommandHelper.CommandExists("xclip") || LinuxCommandHelper.CommandExists("xsel"),
            _ => LinuxCommandHelper.CommandExists(dependency.Command),
        };

    private static bool IsYdotoolSatisfied()
    {
        if (!LinuxCommandHelper.CommandExists("ydotool"))
        {
            return false;
        }

        if (!UsesGnomeWaylandYdotool)
        {
            return true;
        }

        return YdotoolInjectionBackend.IsWorking();
    }
}
