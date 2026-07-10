using echo.Abstractions.Platform;

namespace echo.Platform.Linux.Injection;

internal sealed class X11InjectionBackend : ILinuxInjectionBackend
{
    public string Name => "xdotool";

    public bool IsAvailable =>
        LinuxSession.IsX11
        && LinuxCommandHelper.CommandExists("xdotool")
        && LinuxClipboard.IsAvailable;

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
        if (preferType)
        {
            return TryType(text, typeDelayMs, cancellationToken);
        }

        return TryPaste(text, cancellationToken);
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

            var exitCode = LinuxProcessRunner.RunCommand(
                "xdotool",
                ["key", "--clearmodifiers", "ctrl+v"],
                cancellationToken,
                allowFailure: true,
                out _);

            if (exitCode != 0)
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
            var args = new List<string> { "type", "--clearmodifiers" };
            if (typeDelayMs > 0)
            {
                args.Add("--delay");
                args.Add(typeDelayMs.ToString());
            }

            args.Add("--");
            args.Add(text);

            var exitCode = LinuxProcessRunner.RunCommand(
                "xdotool",
                args,
                cancellationToken,
                allowFailure: true,
                out _);

            return exitCode == 0 ? TextInjectionResult.AutoPasted : null;
        }
        catch
        {
            return null;
        }
    }
}
