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
                    ? "Loc.Linux.Inject.NeedWlClipboard"
                    : "Loc.Linux.Inject.NeedXclip");
        }

        try
        {
            LinuxClipboard.Write(text, cancellationToken);
            return TextInjectionResult.ClipboardOnly("Loc.Linux.Inject.ClipboardOnly");
        }
        catch (Exception ex)
        {
            return TextInjectionResult.Failed(ex.Message);
        }
    }
}
