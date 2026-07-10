namespace echo.Platform.Linux;

internal static class LinuxClipboard
{
    public static bool HasSilentBackend =>
        LinuxSession.IsGnome
        && LinuxSession.IsWayland
        && (LinuxApplicationClipboardBridge.IsRegisteredAndAvailable
            || LinuxCommandHelper.CommandExists("gpaste-client")
            || (HasX11Display() && LinuxCommandHelper.CommandExists("xclip")));

    public static bool IsAvailable =>
        LinuxSession.IsWayland
            ? HasWaylandClipboardTool()
            : LinuxCommandHelper.CommandExists("xclip") || LinuxCommandHelper.CommandExists("xsel");

    public static string? Read(CancellationToken cancellationToken)
    {
        if (LinuxSession.IsWayland)
        {
            if (LinuxSession.IsGnome && LinuxCommandHelper.CommandExists("gpaste-client"))
            {
                return LinuxProcessRunner.RunCommand(
                    "gpaste-client",
                    ["get", "--use-index", "0"],
                    cancellationToken,
                    allowFailure: true);
            }

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
            if (TryWriteGnomeWithoutPanelFlash(text, cancellationToken, out _))
            {
                return;
            }

            if (!LinuxCommandHelper.CommandExists("wl-copy"))
            {
                throw new InvalidOperationException(
                    "No clipboard tool available. Restart Echo or install xclip / gpaste-2.");
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

    private static bool HasWaylandClipboardTool()
    {
        if (LinuxCommandHelper.CommandExists("wl-copy"))
        {
            return true;
        }

        if (LinuxSession.IsGnome)
        {
            if (LinuxApplicationClipboardBridge.IsRegisteredAndAvailable)
            {
                return true;
            }

            if (LinuxCommandHelper.CommandExists("gpaste-client"))
            {
                return true;
            }

            if (HasX11Display() && LinuxCommandHelper.CommandExists("xclip"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryWriteGnomeWithoutPanelFlash(
        string text,
        CancellationToken cancellationToken,
        out string backend)
    {
        backend = string.Empty;
        if (!LinuxSession.IsGnome || !LinuxSession.IsWayland)
        {
            return false;
        }

        if (LinuxApplicationClipboardBridge.TryWrite(text, cancellationToken))
        {
            backend = "avalonia";
            return true;
        }

        if (LinuxCommandHelper.CommandExists("gpaste-client"))
        {
            LinuxProcessRunner.RunCommandWithInput("gpaste-client", ["add"], text, cancellationToken);
            backend = "gpaste-client";
            return true;
        }

        if (HasX11Display() && LinuxCommandHelper.CommandExists("xclip"))
        {
            LinuxProcessRunner.RunCommandWithInput(
                "xclip",
                ["-selection", "clipboard"],
                text,
                cancellationToken);
            backend = "xclip";
            return true;
        }

        return false;
    }

    private static bool HasX11Display() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"));
}
