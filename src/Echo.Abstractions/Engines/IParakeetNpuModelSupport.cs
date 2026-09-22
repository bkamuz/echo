namespace echo.Abstractions.Engines;

public interface IParakeetNpuModelSupport
{
    bool IsRuntimeInstalled { get; }
    bool IsModelInstalled { get; }
    Task EnsureRuntimeAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task DownloadModelAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    void DeleteModel();
}
