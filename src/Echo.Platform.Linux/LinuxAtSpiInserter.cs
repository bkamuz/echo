namespace echo.Platform.Linux;

internal static class LinuxAtSpiInserter
{
    private static readonly Lazy<string?> ScriptPath = new(ResolveScriptPath);
    private static bool? _available;

    public static bool IsAvailable
    {
        get
        {
            if (_available.HasValue)
            {
                return _available.Value;
            }

            _available = Probe();
            return _available.Value;
        }
    }

    public static void ResetProbe()
    {
        _available = null;
    }

    public static bool TryInsert(string text, CancellationToken cancellationToken = default, int typeDelayMs = 0)
    {
        if (!IsAvailable || string.IsNullOrEmpty(text))
        {
            return false;
        }

        try
        {
            var args = new List<string> { ScriptPath.Value! };
            if (typeDelayMs > 0)
            {
                args.Add("--delay-ms");
                args.Add(typeDelayMs.ToString());
            }

            LinuxProcessRunner.RunCommandWithInput(
                "python3",
                args,
                text,
                cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool Probe()
    {
        if (!LinuxCommandHelper.CommandExists("python3"))
        {
            return false;
        }

        if (LinuxSession.IsGnome && LinuxSession.IsWayland)
        {
            // Mutter Wayland does not expose focused widgets to AT-SPI (get_active_descendant is unimplemented).
            return false;
        }

        var script = ScriptPath.Value;
        if (string.IsNullOrWhiteSpace(script) || !File.Exists(script))
        {
            return false;
        }

        var exitCode = LinuxProcessRunner.RunCommand(
            "python3",
            ["-c", "import gi; gi.require_version('Atspi','2.0'); from gi.repository import Atspi"],
            CancellationToken.None,
            allowFailure: true,
            out _);
        return exitCode == 0;
    }

    private static string? ResolveScriptPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "Scripts", "atspi-insert.py"),
            Path.Combine(baseDir, "atspi-insert.py"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
