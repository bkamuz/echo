using echo.Abstractions.Platform;

namespace echo.Platform.Linux.Injection;

internal sealed class WtypeInjectionBackend : ILinuxInjectionBackend
{
    private static readonly object ProbeGate = new();
    private static bool? _wtypeWorks;

    public string Name => "wtype";

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

        var preferType = method == "type";
        return preferType
            ? TryType(text, typeDelayMs, cancellationToken)
            : TryPaste(text, cancellationToken);
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

                _wtypeWorks = ProbeWtype();
                return _wtypeWorks.Value;
            }
        }
    }

    private static bool ProbeWtype()
    {
        if (!LinuxCommandHelper.CommandExists("wtype"))
        {
            return false;
        }

        return LinuxProcessRunner.RunCommand(
            "wtype",
            ["-k", "VoidSymbol"],
            CancellationToken.None,
            allowFailure: true,
            out _) == 0;
    }

    private static TextInjectionResult? TryPaste(string text, CancellationToken cancellationToken)
    {
        string? savedText = null;
        var hadText = false;

        try
        {
            savedText = LinuxClipboard.Read(cancellationToken);
            hadText = !string.IsNullOrEmpty(savedText);
            LinuxClipboard.Write(text, cancellationToken);

            if (!RunWtype(["-M", "ctrl", "-k", "v", "-m", "ctrl"], cancellationToken))
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
