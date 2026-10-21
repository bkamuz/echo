using Avalonia.Threading;
using echo.Abstractions.Core;
using echo.Abstractions.Engines;
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
    private readonly IParakeetNpuModelSupport? _parakeetNpu;
    private readonly IWhisperNpuModelSupport? _whisperNpu;

    public ModelSettingsController(
        ModelDownloader downloader,
        DictationCoordinator coordinator,
        SettingsApplyService applyService,
        AppStatusViewModel status,
        HomeViewModel home,
        LocalizationService loc,
        IParakeetNpuModelSupport? parakeetNpu = null,
        IWhisperNpuModelSupport? whisperNpu = null)
    {
        _whisperNpu = whisperNpu;
        _downloader = downloader;
        _coordinator = coordinator;
        _applyService = applyService;
        _status = status;
        _home = home;
        _loc = loc;
        _parakeetNpu = parakeetNpu;
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

        var downloaded = IsParakeetAssetsReady(spec);
        var statusText = downloaded
            ? _loc.Format("Loc.Model.Loaded", spec.Title)
            : IsNpuRuntimeEngine(spec)
                && spec.IsDownloaded()
                && NpuRuntimeInstalled(spec) == false
                ? _loc.Get("Loc.Status.PreparingNpu")
                : _loc.Format("Loc.Model.NotLoaded", spec.Title);
        return new ModelStatusSnapshot(
            Title: spec.Title,
            StatusText: statusText,
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
        if (spec is null || !NeedsDownload(spec))
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

            if (!IsParakeetAssetsReady(spec))
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

    private bool NeedsDownload(ModelSpec spec)
    {
        if (!IsNpuRuntimeEngine(spec))
        {
            return !spec.IsDownloaded();
        }

        return !IsParakeetAssetsReady(spec);
    }

    private bool IsParakeetAssetsReady(ModelSpec spec)
    {
        if (!IsNpuRuntimeEngine(spec))
        {
            return spec.IsDownloaded();
        }

        var runtimeReady = NpuRuntimeInstalled(spec) != false;
        return spec.IsDownloaded() && runtimeReady;
    }

    private static bool IsNpuRuntimeEngine(ModelSpec spec) =>
        spec.Engine is "parakeet_npu" or "whisper_npu";

    private bool? NpuRuntimeInstalled(ModelSpec spec) =>
        spec.Engine == "whisper_npu" ? _whisperNpu?.IsRuntimeInstalled : _parakeetNpu?.IsRuntimeInstalled;

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
