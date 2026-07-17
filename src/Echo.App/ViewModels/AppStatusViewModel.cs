using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using echo.Abstractions.Core;
using echo.App.Localization;
using echo.Core;
using echo.Core.Update;

namespace echo.App.ViewModels;

public partial class AppStatusViewModel : ObservableObject
{
    private readonly ConfigStore _configStore;
    private readonly LocalizationService _loc;
    private CancellationTokenSource? _clearStatusCts;
    private string? _platformWarningKeyOrText;
    /// <summary>Null = readiness mode; otherwise Loc key / progress token / plain text.</summary>
    private string? _overlayRaw;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isAlert;

    [ObservableProperty]
    private bool _isWarning;

    public AppStatusViewModel(ConfigStore configStore, LocalizationService loc)
    {
        _configStore = configStore;
        _loc = loc;
        _loc.LanguageChanged += (_, _) => OnLanguageChanged();
        StatusText = ReadyStatusText;
    }

    public string ReadyStatusText => _loc.Format("Loc.Status.Ready", UpdateEnvironment.DisplayVersion);

    public string ModelMissingStatusText => _loc.Get("Loc.Status.ModelMissing");

    public void SetPlatformWarning(string? warningKeyOrText)
    {
        _platformWarningKeyOrText = string.IsNullOrWhiteSpace(warningKeyOrText)
            ? null
            : warningKeyOrText.Trim();
        if (_overlayRaw is null && !IsBusy)
        {
            RefreshReadiness();
        }
    }

    public void RefreshReadiness()
    {
        if (IsBusy)
        {
            return;
        }

        _overlayRaw = null;
        var config = _configStore.Load();
        var spec = ModelRegistry.SpecForEngine(config.Engine, config.WhisperModelSize, config.GigaAmModelSize);
        if (spec is not null && !spec.IsDownloaded())
        {
            StatusText = ModelMissingStatusText;
            ApplyStatusTone(alert: true);
            return;
        }

        if (_platformWarningKeyOrText is not null)
        {
            StatusText = _loc.LocText(_platformWarningKeyOrText);
            ApplyStatusTone(warning: true);
            return;
        }

        StatusText = ReadyStatusText;
        ApplyStatusTone();
    }

    /// <param name="keyOrRaw">Loc.* key, WORKING:/DONE: progress token, or plain display text.</param>
    public void SetStatus(string keyOrRaw, bool busy = false, bool alert = false, bool warning = false)
    {
        _clearStatusCts?.Cancel();
        _clearStatusCts?.Dispose();
        _clearStatusCts = null;
        _overlayRaw = keyOrRaw;
        StatusText = ResolveDisplay(keyOrRaw);
        IsBusy = busy;
        ApplyStatusTone(alert, warning);
    }

    public void SetStatusTemporary(string keyOrRaw, int clearAfterMs = 2500, bool alert = false, bool warning = false)
    {
        SetStatus(keyOrRaw, busy: false, alert: alert, warning: warning);
        _clearStatusCts?.Cancel();
        _clearStatusCts?.Dispose();
        _clearStatusCts = new CancellationTokenSource();
        var token = _clearStatusCts.Token;
        _ = ClearAfterDelayAsync(clearAfterMs, token);
    }

    private void OnLanguageChanged()
    {
        if (_overlayRaw is not null)
        {
            StatusText = ResolveDisplay(_overlayRaw);
            return;
        }

        RefreshReadiness();
    }

    private string ResolveDisplay(string keyOrRaw)
    {
        if (ProgressMessages.IsDone(keyOrRaw) || ProgressMessages.TryParseWorking(keyOrRaw, out _, out _))
        {
            return _loc.LocalizeProgress(keyOrRaw);
        }

        return _loc.LocText(keyOrRaw);
    }

    private void ApplyStatusTone(bool alert = false, bool warning = false)
    {
        IsAlert = alert;
        IsWarning = warning && !alert;
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
