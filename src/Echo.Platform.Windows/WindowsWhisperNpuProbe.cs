using echo.Abstractions.Core;
using echo.Abstractions.Engines;

namespace echo.Platform.Windows;

/// <summary>Settings-facing Whisper NPU probe; loads the NPU assembly only when a download is requested.</summary>
public sealed class WindowsWhisperNpuProbe : IWhisperNpuModelSupport
{
    private readonly IServiceProvider _services;
    private IWhisperNpuModelSupport? _loaded;

    public WindowsWhisperNpuProbe(IServiceProvider services)
    {
        _services = services;
    }

    public bool IsRuntimeInstalled =>
        new[] { "runtime-versions.json", "onnxruntime.dll", "onnxruntime_providers_qnn.dll", "QnnHtp.dll", "QnnSystem.dll" }
            .All(f => File.Exists(Path.Combine(AppPaths.NpuWhisperRuntimeDir, f)));

    public bool IsModelInstalled =>
        ModelRegistry.IsWhisperNpuDownloaded(AppPaths.WhisperNpuDir)
        && File.Exists(Path.Combine(AppPaths.WhisperNpuDir, "chipset.txt"));

    public Task EnsureRuntimeAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        ResolveLoaded().EnsureRuntimeAsync(progress, cancellationToken);

    public Task DownloadModelAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        ResolveLoaded().DownloadModelAsync(progress, cancellationToken);

    public void DeleteModel() => ResolveLoaded().DeleteModel();

    private IWhisperNpuModelSupport ResolveLoaded() =>
        _loaded ??= ParakeetAssemblyLoader.CreateWhisperModelSupport(_services);
}
