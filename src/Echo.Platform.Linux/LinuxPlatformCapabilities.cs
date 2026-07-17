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
                "Loc.Linux.Warn.NoSession",
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

        var warningParts = new List<string>();
        if (!supportsHotkey)
        {
            var hotkeyMsg = LinuxHotkeySetup.GetSetupMessage();
            if (!string.IsNullOrEmpty(hotkeyMsg))
            {
                warningParts.Add(hotkeyMsg);
            }
        }

        if (!supportsInject)
        {
            var injectHint = LinuxDependencyCatalog.UsesGnomeWaylandYdotool
                ? "Loc.Linux.Warn.Inject.Gnome"
                : LinuxSession.IsWayland
                    ? "Loc.Linux.Warn.Inject.Wayland"
                    : "Loc.Linux.Warn.Inject.X11";
            warningParts.Add(injectHint);
        }

        if (missing.Count > 0 && canAutoInstall)
        {
            warningParts.Add(warningParts.Count == 0
                ? "Loc.Linux.Warn.MissingPackages"
                : "Loc.Linux.Warn.MissingPackages.Suffix");
        }

        string? warning = warningParts.Count == 0
            ? null
            : string.Join('\u001f', warningParts);

        return new CapabilitySnapshot(supportsHotkey, supportsInject, canAutoInstall, warning, missing);
    }

    private sealed record CapabilitySnapshot(
        bool SupportsGlobalHotkey,
        bool SupportsTextInjection,
        bool CanAutoInstall,
        string? StartupWarning,
        IReadOnlyList<LinuxDependency> MissingDependencies);
}
