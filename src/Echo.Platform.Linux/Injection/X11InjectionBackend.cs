using echo.Abstractions.Platform;

namespace echo.Platform.Linux.Injection;

internal sealed class X11InjectionBackend : ILinuxInjectionBackend
{
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

        return method == "type"
            ? TryType(text, typeDelayMs, cancellationToken)
            : LinuxClipboardPaste.TryPaste(
                text,
                ct => LinuxProcessRunner.RunCommand(
                    "xdotool",
                    ["key", "--clearmodifiers", "ctrl+v"],
                    ct,
                    allowFailure: true,
                    out _) == 0,
                cancellationToken);
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
