using Avalonia.Threading;
using echo.Abstractions.Core;
using echo.App.Localization;
using echo.App.ViewModels;
using echo.Core;

namespace echo.App.Services;

public sealed class ModelSettingsController
{
    private readonly ModelDownloader _downloader;
    private readonly DictationCoordinator _coordinator;
    private readonly SettingsApplyService _applyService;
    private readonly AppStatusViewModel _status;
    private readonly HomeViewModel _home;
    private readonly LocalizationService _loc;

    public ModelSettingsController(
        ModelDownloader downloader,
        DictationCoordinator coordinator,
        SettingsApplyService applyService,
        AppStatusViewModel status,
        HomeViewModel home,
        LocalizationService loc)
    {
        _downloader = downloader;
        _coordinator = coordinator;
        _applyService = applyService;
        _status = status;
        _home = home;
        _loc = loc;
    }

    public ModelSpec? ResolveSpec(string engine, string whisperSize, string gigaAmSize) =>
        ModelRegistry.SpecForEngine(engine, whisperSize, gigaAmSize);

    public ModelStatusSnapshot Refresh(string engine, string whisperSize, string gigaAmSize)
    {
        var spec = ResolveSpec(engine, whisperSize, gigaAmSize);
        if (spec is null)
        {
            return new ModelStatusSnapshot(
                Title: string.Empty,
                StatusText: _loc.Get("Loc.Model.Unknown"),
                IsDownloaded: false,
                HasModel: false);
        }

        var downloaded = spec.IsDownloaded();
        return new ModelStatusSnapshot(
            Title: spec.Title,
            StatusText: downloaded
                ? _loc.Format("Loc.Model.Loaded", spec.Title)
                : _loc.Format("Loc.Model.NotLoaded", spec.Title),
            IsDownloaded: downloaded,
            HasModel: true);
    }

    public async Task<bool> DownloadAsync(
        string engine,
        string whisperSize,
        string gigaAmSize,
        Action<string> setModelStatus,
        Action<bool> setApplying,
        CancellationToken cancellationToken = default)
    {
        var spec = ResolveSpec(engine, whisperSize, gigaAmSize);
        if (spec is null || spec.IsDownloaded())
        {
            return false;
        }

        setApplying(true);
        var downloadRaw = ProgressMessages.Downloading(spec.Title);
        setModelStatus(_loc.LocalizeProgress(downloadRaw));
        _status.SetStatus(downloadRaw, busy: true);
        using var modelBusy = _coordinator.EnterModelBusy();
        try
        {
            var progress = _applyService.CreateProgressReporter(null, status =>
            {
                var isTerminal = ProgressMessages.IsDone(status);
                setModelStatus(_loc.LocalizeProgress(status));
                _status.SetStatus(status, busy: !isTerminal);
            });
            await _downloader.DownloadAsync(spec, progress, cancellationToken).ConfigureAwait(false);

            if (!spec.IsDownloaded())
            {
                throw new InvalidOperationException(
                    $"Download finished but '{spec.Title}' is incomplete. Try again.");
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
                setModelStatus(_loc.Format("Loc.Model.Loaded", spec.Title)));

            var warmupProgress = _applyService.CreateStatusProgress(_applyService.ApplyGeneration);
            await _coordinator.TryWarmupCurrentModelAsync(warmupProgress, cancellationToken)
                .ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _home.NotifyConfigChanged();
                // ponytail: Format args baked in; rare to switch UI lang mid-download toast
                _status.SetStatusTemporary(
                    _loc.Format("Loc.Model.Loaded", spec.Title),
                    SettingsApplyService.StatusClearMs);
            });
            return true;
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                setModelStatus(_loc.Format("Loc.Model.NotLoaded", spec.Title));
                _status.SetStatus("Loc.Status.ApplyError", alert: true);
            });
            return false;
        }
        catch (Exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                setModelStatus(_loc.Format("Loc.Model.NotLoaded", spec.Title));
                _status.SetStatus("Loc.Model.DownloadFailed", alert: true);
            });
            return false;
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => setApplying(false));
        }
    }

    public bool Delete(string engine, string whisperSize, string gigaAmSize, Action<string> setModelStatus)
    {
        var spec = ResolveSpec(engine, whisperSize, gigaAmSize);
        if (spec is null || !spec.IsDownloaded())
        {
            return false;
        }

        _downloader.Delete(spec);
        var deleted = _loc.Format("Loc.Model.Deleted", spec.Title);
        setModelStatus(deleted);
        _status.SetStatusTemporary(deleted, SettingsApplyService.StatusClearMs);
        return true;
    }
}

public sealed record ModelStatusSnapshot(
    string Title,
    string StatusText,
    bool IsDownloaded,
    bool HasModel);
