using Avalonia.Threading;
using echo.Abstractions.Core;
using echo.Core;
using echo.App.ViewModels;

namespace echo.App.Services;

public sealed class ModelSettingsController
{
    private readonly ModelDownloader _downloader;
    private readonly DictationCoordinator _coordinator;
    private readonly SettingsApplyService _applyService;
    private readonly AppStatusViewModel _status;
    private readonly HomeViewModel _home;

    public ModelSettingsController(
        ModelDownloader downloader,
        DictationCoordinator coordinator,
        SettingsApplyService applyService,
        AppStatusViewModel status,
        HomeViewModel home)
    {
        _downloader = downloader;
        _coordinator = coordinator;
        _applyService = applyService;
        _status = status;
        _home = home;
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
                StatusText: "Неизвестная модель",
                IsDownloaded: false,
                HasModel: false);
        }

        var downloaded = spec.IsDownloaded();
        return new ModelStatusSnapshot(
            Title: spec.Title,
            StatusText: downloaded
                ? $"{spec.Title} ✓ загружена"
                : $"{spec.Title} — не загружена",
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
        var downloadLabel = $"Скачивание {spec.Title}…";
        setModelStatus(downloadLabel);
        _status.SetStatus(downloadLabel, busy: true);
        try
        {
            var progress = _applyService.CreateProgressReporter(null, status =>
            {
                var normalized = status.Trim();
                var isTerminal = normalized.StartsWith("Готово", StringComparison.Ordinal);
                setModelStatus(normalized);
                _status.SetStatus(normalized, busy: !isTerminal);
            });
            await _downloader.DownloadAsync(spec, progress, cancellationToken).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => setModelStatus($"{spec.Title} ✓ загружена"));

            var warmupProgress = _applyService.CreateStatusProgress(_applyService.ApplyGeneration);
            await _coordinator.TryWarmupCurrentModelAsync(warmupProgress, cancellationToken)
                .ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _home.NotifyConfigChanged();
                _status.SetStatusTemporary($"{spec.Title} ✓ загружена", SettingsApplyService.StatusClearMs);
            });
            return true;
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
        setModelStatus($"{spec.Title} удалена");
        _status.SetStatusTemporary($"{spec.Title} удалена", SettingsApplyService.StatusClearMs);
        return true;
    }
}

public sealed record ModelStatusSnapshot(
    string Title,
    string StatusText,
    bool IsDownloaded,
    bool HasModel);
