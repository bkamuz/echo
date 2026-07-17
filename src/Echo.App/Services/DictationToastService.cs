using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using echo.Abstractions.Platform;
using echo.App.Localization;
using echo.App.Views;
using echo.Core;

namespace echo.App.Services;

public sealed class DictationToastService : IDictationResultNotifier, IDisposable
{
    private const int AutoDismissMs = 7000;

    private readonly ConfigStore _configStore;
    private readonly LocalizationService _loc;
    private readonly IUserStatusNotifier? _statusNotifier;
    private DictationToastWindow? _window;
    private CancellationTokenSource? _dismissCts;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private EventHandler<ControlledApplicationLifetimeExitEventArgs>? _exitHandler;
    private bool _disposed;

    public DictationToastService(
        ConfigStore configStore,
        LocalizationService loc,
        IUserStatusNotifier? statusNotifier = null)
    {
        _configStore = configStore;
        _loc = loc;
        _statusNotifier = statusNotifier;
    }

    public void Show(string text)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (!_configStore.Load().ShowDictationToast)
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
        if (_disposed)
        {
            return;
        }

        _dismissCts?.Cancel();
        _dismissCts?.Dispose();
        _dismissCts = new CancellationTokenSource();
        var token = _dismissCts.Token;

        if (!EnsureWindow())
        {
            return;
        }

        _window!.Present(text);
        _ = AutoDismissAsync(token);
    }

    private bool EnsureWindow()
    {
        if (_window is not null)
        {
            return true;
        }

        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return false;
        }

        _desktop = desktop;
        _window = new DictationToastWindow();
        _window.CopyRequested += OnWindowCopyRequested;
        _exitHandler = OnDesktopExit;
        desktop.Exit += _exitHandler;
        return true;
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e) =>
        DisposeWindowResources();

    private void OnWindowCopyRequested(object? sender, string text) =>
        _ = CopyAndDismissAsync(text);

    private async Task CopyAndDismissAsync(string text)
    {
        try
        {
            var clipboard = _window?.Clipboard;
            if (clipboard is null)
            {
                return;
            }

            await clipboard.SetTextAsync(text).ConfigureAwait(true);
            _statusNotifier?.ShowTemporary(_loc.Get("Loc.Status.Copied"));
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

    private void DisposeWindowResources()
    {
        CancelAutoDismiss();

        if (_window is not null)
        {
            _window.CopyRequested -= OnWindowCopyRequested;
            _window.Close();
            _window = null;
        }

        if (_desktop is not null && _exitHandler is not null)
        {
            _desktop.Exit -= _exitHandler;
            _exitHandler = null;
            _desktop = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeWindowResources();
    }
}
