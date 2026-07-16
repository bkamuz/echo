using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using echo.Abstractions.Platform;
using echo.App.Views;

namespace echo.App.Services;

public sealed class DictationToastService : IDictationResultNotifier
{
    private const int AutoDismissMs = 7000;

    private readonly IUserStatusNotifier? _statusNotifier;
    private DictationToastWindow? _window;
    private CancellationTokenSource? _dismissCts;
    private bool _exitHooked;

    public DictationToastService(IUserStatusNotifier? statusNotifier = null)
    {
        _statusNotifier = statusNotifier;
    }

    public void Show(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            ShowOnUi(text);
            return;
        }

        Dispatcher.UIThread.Post(() => ShowOnUi(text));
    }

    private void ShowOnUi(string text)
    {
        _dismissCts?.Cancel();
        _dismissCts?.Dispose();
        _dismissCts = new CancellationTokenSource();
        var token = _dismissCts.Token;

        EnsureWindow();
        _window!.Present(text, CopyAndDismissAsync);

        _ = AutoDismissAsync(token);
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        _window = new DictationToastWindow();
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        if (_exitHooked)
        {
            return;
        }

        _exitHooked = true;
        desktop.Exit += (_, _) =>
        {
            CancelAutoDismiss();
            if (_window is null)
            {
                return;
            }

            _window.Close();
            _window = null;
        };
    }

    private async Task CopyAndDismissAsync(string text)
    {
        try
        {
            var clipboard = _window?.Clipboard
                ?? (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                    ?.MainWindow?.Clipboard;

            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(text).ConfigureAwait(true);
                _statusNotifier?.ShowTemporary("Скопировано");
            }
        }
        finally
        {
            CancelAutoDismiss();
            _window?.Dismiss();
        }
    }

    private async Task AutoDismissAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(AutoDismissMs, token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => _window?.Dismiss());
        }
        catch (OperationCanceledException)
        {
            // Replaced by a newer toast or dismissed manually.
        }
    }

    private void CancelAutoDismiss()
    {
        _dismissCts?.Cancel();
        _dismissCts?.Dispose();
        _dismissCts = null;
    }
}
