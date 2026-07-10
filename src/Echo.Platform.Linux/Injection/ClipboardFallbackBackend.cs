using echo.Abstractions.Platform;

namespace echo.Platform.Linux.Injection;

internal sealed class ClipboardFallbackBackend : ILinuxInjectionBackend
{
    public bool IsAvailable => LinuxClipboard.IsAvailable;

    public TextInjectionResult? TryInject(
        string text,
        string method,
        int typeDelayMs,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return TextInjectionResult.Failed(
                LinuxSession.IsWayland
                    ? "Установите wl-clipboard для вставки текста."
                    : "Установите xclip или xsel для вставки текста.");
        }

        try
        {
            LinuxClipboard.Write(text, cancellationToken);
            return TextInjectionResult.ClipboardOnly("Текст скопирован — нажмите Ctrl+V.");
        }
        catch (Exception ex)
        {
            return TextInjectionResult.Failed(ex.Message);
        }
    }
}
