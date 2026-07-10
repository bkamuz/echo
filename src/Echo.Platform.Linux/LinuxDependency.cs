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
    /// Mutter on GNOME Wayland does not expose focused widgets to AT-SPI; ydotool is used instead.
    /// </summary>
    public static bool RequiresAtSpiPackages =>
        !(LinuxSession.IsWayland && LinuxSession.IsGnome);

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
                "Захват микрофона (arecord)",
                "Нужен для записи голоса во время диктовки.",
                "arecord",
                AlsaPackages),
        };

        if (RequiresAtSpiPackages)
        {
            dependencies.Add(new(
                "python3-gi",
                "Python AT-SPI (python3-gi)",
                "Нужен для автоматической вставки текста через специальные возможности.",
                "python3",
                PythonGiPackages));
            dependencies.Add(new(
                "atspi",
                "AT-SPI (gir1.2-atspi-2.0)",
                "Нужен для вставки текста в поля ввода других приложений.",
                "python3",
                AtspiPackages));
        }

        if (LinuxSession.IsWayland)
        {
            dependencies.Add(new(
                "wl-copy",
                "Буфер обмена Wayland (wl-clipboard)",
                "Нужен для копирования распознанного текста в буфер обмена.",
                "wl-copy",
                WlClipboardPackages));
            if (LinuxSession.IsGnome)
            {
                dependencies.Add(new(
                    "ydotool",
                    "Эмуляция клавиатуры GNOME Wayland (ydotool)",
                    "Нужен для автовставки Ctrl+V в GNOME на Wayland (требуется ydotoold и группа input).",
                    "ydotool",
                    YdotoolPackages));
            }
            else
            {
                dependencies.Add(new(
                    "wtype",
                    "Эмуляция клавиатуры Wayland (wtype)",
                    "Нужен для автовставки в Sway, Hyprland и других wlroots-композиторах.",
                    "wtype",
                    WtypePackages));
            }
        }

        if (LinuxSession.IsX11)
        {
            dependencies.Add(new(
                "xclip",
                "Буфер обмена X11 (xclip)",
                "Нужен для копирования распознанного текста в буфер обмена.",
                "xclip",
                XclipPackages));
            dependencies.Add(new(
                "xdotool",
                "Эмуляция клавиатуры X11 (xdotool)",
                "Нужен для автоматической вставки текста в X11-сессии.",
                "xdotool",
                XdotoolPackages));
        }

        dependencies.Add(new(
            "sg",
            "Группа input без перелогина (sg)",
            "Нужен для глобального хоткея, если вы уже в группе input, но ещё не перезаходили в сессию.",
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
            .Where(dependency => dependency.Id is not ("wtype" or "ydotool" or "sg"))
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
            "wl-copy" => LinuxCommandHelper.CommandExists("wl-copy"),
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

        return LinuxInjectionChain.ProbeBackends()
            .Any(probe => probe.Name == "ydotool" && probe.Available);
    }
}
