using Avalonia.Threading;
using echo.App.ViewModels;
using echo.Core;

namespace echo.App.Services;

public sealed class SettingsApplyService
{
    public const int ApplyDebounceMs = 300;
    public const int StatusClearMs = 2500;

    private readonly DictationCoordinator _coordinator;
    private readonly AppStatusViewModel _status;
    private int _applyGeneration;
    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _applyCts;

    public SettingsApplyService(DictationCoordinator coordinator, AppStatusViewModel status)
    {
        _coordinator = coordinator;
        _status = status;
    }

    public int ApplyGeneration => _applyGeneration;

    public void ScheduleApply(Func<Task<AppConfig>> prepareConfigAsync, Action onSuccess, Action<int> onApplyFinished)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        var debounceToken = _debounceCts.Token;
        _ = DebouncedApplyAsync(prepareConfigAsync, onSuccess, onApplyFinished, debounceToken);
    }

    public async Task ApplyAsync(
        Func<Task<AppConfig>> prepareConfigAsync,
        Action onSuccess,
        Action<int> onApplyFinished)
    {
        var generation = ++_applyGeneration;
        _applyCts?.Cancel();
        _applyCts?.Dispose();
        _applyCts = new CancellationTokenSource();
        var ct = _applyCts.Token;

        var config = await prepareConfigAsync().ConfigureAwait(false);

        try
        {
            var progress = CreateStatusProgress(generation);
            await _coordinator.SaveConfigAsync(config, progress, ct).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation != _applyGeneration)
                {
                    return;
                }

                onSuccess();
                _status.SetStatusTemporary("Готово", StatusClearMs);
            });
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation != _applyGeneration)
                {
                    return;
                }

                _status.SetStatusTemporary(AppStatusViewModel.ModelMissingStatus, StatusClearMs, alert: true);
            });
        }
        catch (Exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation != _applyGeneration)
                {
                    return;
                }

                _status.SetStatusTemporary("Ошибка применения настроек", StatusClearMs);
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => onApplyFinished(generation));
        }
    }

    public IProgress<string> CreateStatusProgress(int? generation) =>
        CreateProgressReporter(generation, ReportApplyProgress);

    public IProgress<string> CreateProgressReporter(int? generation, Action<string> apply) =>
        new Progress<string>(status =>
        {
            if (generation.HasValue && generation.Value != _applyGeneration)
            {
                return;
            }

            void Run() => apply(status);
            if (Dispatcher.UIThread.CheckAccess())
            {
                Run();
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (generation.HasValue && generation.Value != _applyGeneration)
                    {
                        return;
                    }

                    Run();
                });
            }
        });

    private async Task DebouncedApplyAsync(
        Func<Task<AppConfig>> prepareConfigAsync,
        Action onSuccess,
        Action<int> onApplyFinished,
        CancellationToken debounceToken)
    {
        try
        {
            await Task.Delay(ApplyDebounceMs, debounceToken).ConfigureAwait(false);
            await ApplyAsync(prepareConfigAsync, onSuccess, onApplyFinished).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ReportApplyProgress(string status)
    {
        var normalized = status.Trim();
        if (normalized.StartsWith("Готово", StringComparison.Ordinal))
        {
            return;
        }

        _status.SetStatus(normalized, busy: true);
    }
}
