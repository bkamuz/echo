using echo.Abstractions.Platform;

namespace echo.Platform.Linux;

internal static class LinuxClipboardPaste
{
    internal static TextInjectionResult? TryPaste(
        string text,
        Func<CancellationToken, bool> sendPaste,
        CancellationToken cancellationToken,
        bool restoreClipboard = true)
    {
        string? savedText = null;
        var hadText = false;

        try
        {
            if (restoreClipboard)
            {
                savedText = LinuxClipboard.Read(cancellationToken);
                hadText = !string.IsNullOrEmpty(savedText);
            }

            LinuxClipboard.Write(text, cancellationToken);
            if (!sendPaste(cancellationToken))
            {
                return null;
            }

            if (restoreClipboard && hadText && savedText is not null)
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
}
