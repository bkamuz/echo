namespace echo.Platform.Linux;

internal static class LinuxClipboard
{
    public static bool IsAvailable =>
        LinuxSession.IsWayland
            ? LinuxCommandHelper.CommandExists("wl-copy")
            : LinuxCommandHelper.CommandExists("xclip") || LinuxCommandHelper.CommandExists("xsel");

    public static string? Read(CancellationToken cancellationToken)
    {
        if (LinuxSession.IsWayland)
        {
            if (!LinuxCommandHelper.CommandExists("wl-paste"))
            {
                return null;
            }

            return LinuxProcessRunner.RunCommand("wl-paste", ["-n"], cancellationToken, allowFailure: true);
        }

        if (LinuxCommandHelper.CommandExists("xclip"))
        {
            return LinuxProcessRunner.RunCommand(
                "xclip",
                ["-selection", "clipboard", "-o"],
                cancellationToken,
                allowFailure: true);
        }

        if (LinuxCommandHelper.CommandExists("xsel"))
        {
            return LinuxProcessRunner.RunCommand(
                "xsel",
                ["--clipboard", "--output"],
                cancellationToken,
                allowFailure: true);
        }

        return null;
    }

    public static void Write(string text, CancellationToken cancellationToken)
    {
        if (LinuxSession.IsWayland)
        {
            if (!LinuxCommandHelper.CommandExists("wl-copy"))
            {
                throw new InvalidOperationException("wl-copy is not installed.");
            }

            LinuxProcessRunner.RunCommandWithInput("wl-copy", [], text, cancellationToken);
            return;
        }

        if (LinuxCommandHelper.CommandExists("xclip"))
        {
            LinuxProcessRunner.RunCommandWithInput(
                "xclip",
                ["-selection", "clipboard"],
                text,
                cancellationToken);
            return;
        }

        if (LinuxCommandHelper.CommandExists("xsel"))
        {
            LinuxProcessRunner.RunCommandWithInput(
                "xsel",
                ["--clipboard", "--input"],
                text,
                cancellationToken);
            return;
        }

        throw new InvalidOperationException("No clipboard tool available (install xclip or xsel).");
    }
}
