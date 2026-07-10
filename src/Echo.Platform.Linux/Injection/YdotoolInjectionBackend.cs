using echo.Abstractions.Platform;

namespace echo.Platform.Linux.Injection;

internal sealed class YdotoolInjectionBackend : ILinuxInjectionBackend
{
    private static readonly object ProbeGate = new();
    private static bool? _ydotoolWorks;

    public string Name => "ydotool";

    public bool IsAvailable =>
        LinuxSession.IsWayland
        && LinuxSession.IsGnome
        && LinuxClipboard.IsAvailable
        && YdotoolWorks;

    public static void ResetProbe()
    {
        lock (ProbeGate)
        {
            _ydotoolWorks = null;
        }
    }

    public TextInjectionResult? TryInject(
        string text,
        string method,
        int typeDelayMs,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return null;
        }

        return TryPaste(text, cancellationToken);
    }

    private static bool YdotoolWorks
    {
        get
        {
            lock (ProbeGate)
            {
                if (_ydotoolWorks.HasValue)
                {
                    return _ydotoolWorks.Value;
                }

                _ydotoolWorks = ProbeYdotool();
                return _ydotoolWorks.Value;
            }
        }
    }

    private static bool ProbeYdotool()
    {
        if (!LinuxCommandHelper.CommandExists("ydotool"))
        {
            return false;
        }

        return LinuxProcessRunner.RunCommand(
            "ydotool",
            ["key", "0:0"],
            CancellationToken.None,
            allowFailure: true,
            out _) == 0;
    }

    private static TextInjectionResult? TryPaste(string text, CancellationToken cancellationToken)
    {
        try
        {
            if (LinuxSession.IsGnome && LinuxSession.IsWayland)
            {
                LinuxClipboard.Write(text, cancellationToken);
                return RunYdotool(["key", "29:1", "47:1", "47:0", "29:0"], cancellationToken)
                    ? TextInjectionResult.AutoPasted
                    : null;
            }

            string? savedText = null;
            var hadText = false;

            savedText = LinuxClipboard.Read(cancellationToken);
            hadText = !string.IsNullOrEmpty(savedText);
            LinuxClipboard.Write(text, cancellationToken);

            if (!RunYdotool(["key", "29:1", "47:1", "47:0", "29:0"], cancellationToken))
            {
                return null;
            }

            if (hadText && savedText is not null)
            {
                LinuxClipboard.Write(savedText, cancellationToken);
            }

            return TextInjectionResult.AutoPasted;
        }
        catch
        {
            return null;
        }
    }

    private static bool RunYdotool(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        LinuxProcessRunner.RunCommand("ydotool", arguments, cancellationToken, allowFailure: true, out _) == 0;
}
