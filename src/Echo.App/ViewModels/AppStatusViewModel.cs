using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using echo.Abstractions.Core;
using echo.Core;

namespace echo.App.ViewModels;

public partial class AppStatusViewModel : ObservableObject
{
    public const string ReadyStatus = "Echo готов";
    public const string ModelMissingStatus = "Модель не загружена — скачайте в «Настройках»";

    private readonly ConfigStore _configStore;
    private CancellationTokenSource? _clearStatusCts;
    private string? _platformWarning;

    [ObservableProperty]
    private string _statusText = ReadyStatus;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isAlert;

    public AppStatusViewModel(ConfigStore configStore)
    {
        _configStore = configStore;
    }

    public void SetPlatformWarning(string? warning)
    {
        _platformWarning = string.IsNullOrWhiteSpace(warning) ? null : warning.Trim();
        RefreshReadiness();
    }

    public void RefreshReadiness()
    {
        if (IsBusy)
        {
            return;
        }

        var config = _configStore.Load();
        var spec = ModelRegistry.SpecForEngine(config.Engine, config.WhisperModelSize, config.GigaAmModelSize);
        if (spec is not null && !spec.IsDownloaded())
        {
            StatusText = ModelMissingStatus;
            IsAlert = true;
            return;
        }

        if (_platformWarning is not null)
        {
            StatusText = _platformWarning;
            IsAlert = false;
            return;
        }

        StatusText = ReadyStatus;
        IsAlert = false;
    }

    public void SetStatus(string text, bool busy = false, bool alert = false)
    {
        _clearStatusCts?.Cancel();
        _clearStatusCts?.Dispose();
        _clearStatusCts = null;
        StatusText = text;
        IsBusy = busy;
        IsAlert = alert;
    }

    public void SetStatusTemporary(string text, int clearAfterMs = 2500, bool alert = false)
    {
        SetStatus(text, busy: false, alert: alert);
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
                    RefreshReadiness();
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
    }
}
