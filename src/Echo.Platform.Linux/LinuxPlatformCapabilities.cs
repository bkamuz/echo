using echo.Platform.Linux.Injection;

namespace echo.Platform.Linux;

public static class LinuxPlatformCapabilities
{
    private static readonly object Gate = new();
    private static CapabilitySnapshot? _snapshot;

    public static bool SupportsGlobalHotkey => GetSnapshot().SupportsGlobalHotkey;

    public static bool SupportsTextInjection => GetSnapshot().SupportsTextInjection;

    public static string? StartupWarning => GetSnapshot().StartupWarning;

    public static IReadOnlyList<LinuxDependency> MissingDependencies => GetSnapshot().MissingDependencies;

    public static bool CanAutoInstall => GetSnapshot().CanAutoInstall;

    public static bool IsFlatpakSandbox => LinuxCommandHelper.IsFlatpakSandbox();

    public static void PrepareForDependencyPrompt() => Refresh();

    public static void Refresh()
    {
        lock (Gate)
        {
            _snapshot = null;
        }

        LinuxInjectionChain.ResetProbes();
    }

    private static CapabilitySnapshot GetSnapshot()
    {
        if (_snapshot is not null)
        {
            return _snapshot;
        }

        lock (Gate)
        {
            return _snapshot ??= BuildSnapshot();
        }
    }

    private static CapabilitySnapshot BuildSnapshot()
    {
        if (!OperatingSystem.IsLinux())
        {
            return new CapabilitySnapshot(false, false, false, null, []);
        }

        if (LinuxSession.Type == LinuxSessionType.Unknown)
        {
            return new CapabilitySnapshot(
                false,
                false,
                false,
                "Не удалось определить графическую сессию (нужен Wayland или X11).",
                []);
        }

        var missing = LinuxDependencyCatalog.GetMissing();
        var supportsHotkey = LinuxEvdevNative.CanAccessKeyboardDevices()
            || LinuxHotkeyBridgeLauncher.CanLaunch();
        var supportsInject = LinuxClipboard.IsAvailable
            || LinuxAtSpiInserter.IsAvailable
            || LinuxInjectionChain.HasAutoInjectionBackend;
        var canAutoInstall = !IsFlatpakSandbox
            && LinuxPackageManagerDetector.CanElevateInstall()
            && missing.Any(dependency => dependency.GetPackageName(LinuxPackageManagerDetector.Detect()) is not null);

        string? warning = null;
        if (!supportsHotkey)
        {
            warning = LinuxHotkeySetup.GetSetupMessage();
        }

        if (!supportsInject)
        {
            var injectHint = LinuxDependencyCatalog.UsesGnomeWaylandYdotool
                ? "Установите wl-clipboard и ydotool (с ydotoold) для автовставки на GNOME Wayland."
                : LinuxSession.IsWayland
                    ? "Установите wl-clipboard и python3-gi для вставки текста."
                    : "Установите xclip и xdotool для вставки текста.";
            warning = string.IsNullOrWhiteSpace(warning)
                ? injectHint
                : warning + " " + injectHint;
        }

        if (missing.Count > 0 && canAutoInstall)
        {
            warning = string.IsNullOrWhiteSpace(warning)
                ? "Для диктовки нужны системные компоненты — Echo может установить их при запуске."
                : warning + " Echo может установить недостающие пакеты при запуске.";
        }

        return new CapabilitySnapshot(supportsHotkey, supportsInject, canAutoInstall, warning, missing);
    }

    private sealed record CapabilitySnapshot(
        bool SupportsGlobalHotkey,
        bool SupportsTextInjection,
        bool CanAutoInstall,
        string? StartupWarning,
        IReadOnlyList<LinuxDependency> MissingDependencies);
}
