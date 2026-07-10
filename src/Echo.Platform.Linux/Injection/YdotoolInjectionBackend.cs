using echo.Abstractions.Platform;

namespace echo.Platform.Linux.Injection;

internal sealed class YdotoolInjectionBackend : ILinuxInjectionBackend
{
    private static readonly object ProbeGate = new();
    private static bool? _ydotoolWorks;

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

        if (LinuxSession.IsGnome && LinuxSession.IsWayland)
        {
            try
            {
                LinuxClipboard.Write(text, cancellationToken);
                return RunYdotool(["key", "29:1", "47:1", "47:0", "29:0"], cancellationToken)
                    ? TextInjectionResult.AutoPasted
                    : null;
            }
            catch
            {
                return null;
            }
        }

        return LinuxClipboardPaste.TryPaste(
            text,
            ct => RunYdotool(["key", "29:1", "47:1", "47:0", "29:0"], ct),
            cancellationToken);
    }

    public static bool IsWorking()
    {
        lock (ProbeGate)
        {
            if (_ydotoolWorks.HasValue)
            {
                return _ydotoolWorks.Value;
            }

            _ydotoolWorks = LinuxCommandHelper.CommandExists("ydotool")
                && LinuxProcessRunner.RunCommand(
                    "ydotool",
                    ["key", "0:0"],
                    CancellationToken.None,
                    allowFailure: true,
                    out _) == 0;
            return _ydotoolWorks.Value;
        }
    }

    private static bool YdotoolWorks => IsWorking();

    private static bool RunYdotool(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        LinuxProcessRunner.RunCommand("ydotool", arguments, cancellationToken, allowFailure: true, out _) == 0;
}
