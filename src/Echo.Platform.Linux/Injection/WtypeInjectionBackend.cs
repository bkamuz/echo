using echo.Abstractions.Platform;

namespace echo.Platform.Linux.Injection;

internal sealed class WtypeInjectionBackend : ILinuxInjectionBackend
{
    private static readonly object ProbeGate = new();
    private static bool? _wtypeWorks;

    public bool IsAvailable =>
        LinuxSession.IsWayland
        && LinuxSession.IsWlroots
        && LinuxClipboard.IsAvailable
        && WtypeWorks;

    public static void ResetProbe()
    {
        lock (ProbeGate)
        {
            _wtypeWorks = null;
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

        return method == "type"
            ? TryType(text, typeDelayMs, cancellationToken)
            : LinuxClipboardPaste.TryPaste(
                text,
                ct => RunWtype(["-M", "ctrl", "-k", "v", "-m", "ctrl"], ct),
                cancellationToken);
    }

    private static bool WtypeWorks
    {
        get
        {
            lock (ProbeGate)
            {
                if (_wtypeWorks.HasValue)
                {
                    return _wtypeWorks.Value;
                }

                _wtypeWorks = LinuxCommandHelper.CommandExists("wtype")
                    && LinuxProcessRunner.RunCommand(
                        "wtype",
                        ["-k", "VoidSymbol"],
                        CancellationToken.None,
                        allowFailure: true,
                        out _) == 0;
                return _wtypeWorks.Value;
            }
        }
    }

    private static TextInjectionResult? TryType(string text, int typeDelayMs, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var ch in text)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ch == '\r')
                {
                    continue;
                }

                var args = ch == '\n' ? new[] { "-k", "Return" } : new[] { ch.ToString() };
                if (!RunWtype(args, cancellationToken))
                {
                    return null;
                }

                if (typeDelayMs > 0)
                {
                    Thread.Sleep(typeDelayMs);
                }
            }

            return TextInjectionResult.AutoPasted;
        }
        catch
        {
            return null;
        }
    }

    private static bool RunWtype(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        LinuxProcessRunner.RunCommand("wtype", arguments, cancellationToken, allowFailure: true, out _) == 0;
}
