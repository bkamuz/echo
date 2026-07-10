using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using echo.Abstractions.Platform;
using echo.Platform.Linux;

namespace echo.App.Services;

public sealed class AvaloniaApplicationClipboard : IApplicationClipboard
{
    private readonly Func<TopLevel?> _getTopLevel;

    public AvaloniaApplicationClipboard(Func<TopLevel?> getTopLevel)
    {
        _getTopLevel = getTopLevel;
    }

    public bool IsAvailable =>
        OperatingSystem.IsLinux()
        && LinuxSession.IsGnome
        && LinuxSession.IsWayland
        && _getTopLevel()?.Clipboard is not null;

    public async ValueTask SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Dispatcher.UIThread.CheckAccess())
        {
            await SetOnUiThreadAsync(text).ConfigureAwait(false);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(async () => await SetOnUiThreadAsync(text)).ConfigureAwait(false);
    }

    private async Task SetOnUiThreadAsync(string text)
    {
        var clipboard = _getTopLevel()?.Clipboard;
        if (clipboard is null)
        {
            throw new InvalidOperationException("Application clipboard is not available.");
        }

        await clipboard.SetTextAsync(text).ConfigureAwait(true);
    }
}
