using echo.Abstractions.Engines;

namespace echo.Engines.ParakeetNpu;

public sealed class ParakeetNpuModelSupport : IParakeetNpuModelSupport
{
    private readonly QnnRuntimeDownloader _runtimeDownloader;
    private readonly ParakeetModelDownloader _modelDownloader;

    public ParakeetNpuModelSupport(
        QnnRuntimeDownloader runtimeDownloader,
        ParakeetModelDownloader modelDownloader)
    {
        _runtimeDownloader = runtimeDownloader;
        _modelDownloader = modelDownloader;
    }

    public bool IsRuntimeInstalled => _runtimeDownloader.IsInstalled;

    public bool IsModelInstalled => _modelDownloader.IsInstalled;

    public Task EnsureRuntimeAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        _runtimeDownloader.EnsureInstalledAsync(progress, cancellationToken);

    public Task DownloadModelAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        _modelDownloader.EnsureInstalledAsync(progress, cancellationToken);

    public void DeleteModel() => _modelDownloader.Delete();
}
