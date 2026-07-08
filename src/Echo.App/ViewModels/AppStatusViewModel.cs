using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace echo.App.ViewModels;

public partial class AppStatusViewModel : ObservableObject
{
    public const string ReadyStatus = "Echo готов";

    private CancellationTokenSource? _clearStatusCts;

    [ObservableProperty]
    private string _statusText = ReadyStatus;

    [ObservableProperty]
    private bool _isBusy;

    public void SetStatus(string text, bool busy = false)
    {
        _clearStatusCts?.Cancel();
        _clearStatusCts?.Dispose();
        _clearStatusCts = null;
        StatusText = text;
        IsBusy = busy;
    }

    public void SetStatusTemporary(string text, int clearAfterMs = 2500)
    {
        SetStatus(text, busy: false);
        _clearStatusCts?.Cancel();
        _clearStatusCts?.Dispose();
        _clearStatusCts = new CancellationTokenSource();
        var token = _clearStatusCts.Token;
        _ = ClearAfterDelayAsync(clearAfterMs, token);
    }

    private async Task ClearAfterDelayAsync(int delayMs, CancellationToken token)
    {
        try
        {
            await Task.Delay(delayMs, token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsBusy)
                {
                    StatusText = ReadyStatus;
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
    }
}
