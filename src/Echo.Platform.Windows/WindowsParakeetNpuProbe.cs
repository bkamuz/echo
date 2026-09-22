using echo.Abstractions.Engines;
using Microsoft.Extensions.DependencyInjection;

namespace echo.Platform.Windows;

/// <summary>
/// Settings-facing Parakeet probe that avoids loading Parakeet/ORT until runtime install is requested.
/// </summary>
public sealed class WindowsParakeetNpuProbe : IParakeetNpuModelSupport
{
    private readonly IServiceProvider _services;
    private IParakeetNpuModelSupport? _loaded;

    public WindowsParakeetNpuProbe(IServiceProvider services)
    {
        _services = services;
    }

    public bool IsRuntimeInstalled => ParakeetRuntimeProbe.IsRuntimeInstalled;

    public bool IsModelInstalled => ParakeetRuntimeProbe.IsModelInstalled;

    public Task EnsureRuntimeAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        ResolveLoaded().EnsureRuntimeAsync(progress, cancellationToken);

    public Task DownloadModelAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        ResolveLoaded().DownloadModelAsync(progress, cancellationToken);

    public void DeleteModel() => ResolveLoaded().DeleteModel();

    private IParakeetNpuModelSupport ResolveLoaded() =>
        _loaded ??= ParakeetAssemblyLoader.CreateModelSupport(_services);
}
