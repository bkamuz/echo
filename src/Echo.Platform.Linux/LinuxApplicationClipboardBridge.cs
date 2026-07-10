using echo.Abstractions.Platform;

namespace echo.Platform.Linux;

/// <summary>
/// Set from Echo.App after the main window exists. Uses Avalonia's Wayland clipboard
/// without spawning wl-copy (no GNOME panel flash).
/// </summary>
public static class LinuxApplicationClipboardBridge
{
    private static IApplicationClipboard? _clipboard;

    public static void Register(IApplicationClipboard? clipboard)
    {
        _clipboard = clipboard;
    }

    public static bool IsRegisteredAndAvailable =>
        _clipboard is { IsAvailable: true };

    public static bool TryWrite(string text, CancellationToken cancellationToken)
    {
        if (_clipboard is not { IsAvailable: true })
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _clipboard.SetTextAsync(text, cancellationToken).AsTask().GetAwaiter().GetResult();
        return true;
    }
}
