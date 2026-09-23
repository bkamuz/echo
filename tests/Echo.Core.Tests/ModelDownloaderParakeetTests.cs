using echo.Abstractions.Core;
using echo.Abstractions.Engines;
using echo.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace echo.Core.Tests;

public class ModelDownloaderParakeetTests
{
    [Fact]
    public async Task EnsureParakeetNpuAssetsAsync_AlwaysEnsuresRuntimeWhenRequested()
    {
        var support = new RecordingParakeetSupport { IsRuntimeInstalled = false };
        var downloader = new ModelDownloader(
            NullLogger<ModelDownloader>.Instance,
            new HttpClient(),
            parakeetNpu: support);

        await downloader.EnsureParakeetNpuAssetsAsync(
            ensureRuntime: true,
            ensureModel: false,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, support.EnsureRuntimeCalls);
        Assert.Equal(0, support.DownloadModelCalls);
    }

    [Fact]
    public async Task EnsureParakeetNpuAssetsAsync_SkipsRuntimeWhenAlreadyInstalled()
    {
        var support = new RecordingParakeetSupport { IsRuntimeInstalled = true };
        var downloader = new ModelDownloader(
            NullLogger<ModelDownloader>.Instance,
            new HttpClient(),
            parakeetNpu: support);

        await downloader.EnsureParakeetNpuAssetsAsync(
            ensureRuntime: true,
            ensureModel: false,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, support.EnsureRuntimeCalls);
    }

    private sealed class RecordingParakeetSupport : IParakeetNpuModelSupport
    {
        public bool IsRuntimeInstalled { get; init; }

        public bool IsModelInstalled => false;

        public int EnsureRuntimeCalls { get; private set; }

        public int DownloadModelCalls { get; private set; }

        public Task EnsureRuntimeAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            EnsureRuntimeCalls++;
            return Task.CompletedTask;
        }

        public Task DownloadModelAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            DownloadModelCalls++;
            return Task.CompletedTask;
        }

        public void DeleteModel()
        {
        }
    }
}
