using echo.Abstractions.Core;
using Microsoft.Extensions.Logging;

namespace echo.Engines.ParakeetNpu;

public sealed class ParakeetModelDownloader
{
    private readonly HttpClient _http;
    private readonly ILogger<ParakeetModelDownloader> _logger;

    public ParakeetModelDownloader(HttpClient http, ILogger<ParakeetModelDownloader> logger)
    {
        _http = http;
        _logger = logger;
    }

    public static string ModelDir => AppPaths.ParakeetNpuDir;

    public bool IsInstalled => ParakeetNpuPaths.IsModelInstalled;

    public async Task EnsureInstalledAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsInstalled)
        {
            return;
        }

        var manifest = ManifestLoader.LoadModelManifest();
        Directory.CreateDirectory(ModelDir);

        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(ModelDir, file.Path);
            if (File.Exists(target))
            {
                try
                {
                    AssetVerifier.VerifyFile(target, file.Bytes, file.Sha256);
                    continue;
                }
                catch (InvalidOperationException)
                {
                    File.Delete(target);
                }
            }

            progress?.Report(ProgressMessages.Downloading(file.Path));
            _logger.LogInformation("Downloading Parakeet NPU asset {File} from {Url}", file.Path, file.Url);

            var tmp = target + ".tmp";
            using var response = await _http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var fileStream = File.Create(tmp))
            {
                await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tmp, target, overwrite: true);
            AssetVerifier.VerifyFile(target, file.Bytes, file.Sha256);
        }

        if (!IsInstalled)
        {
            throw new InvalidOperationException("Parakeet NPU model download finished but verification failed.");
        }

        progress?.Report(ProgressMessages.Done("Parakeet NPU model"));
        _logger.LogInformation("Parakeet NPU model installed to {Dir}", ModelDir);
    }

    public void Delete()
    {
        if (Directory.Exists(ModelDir))
        {
            Directory.Delete(ModelDir, recursive: true);
            _logger.LogInformation("Deleted Parakeet NPU model at {Dir}", ModelDir);
        }
    }
}

internal static class ParakeetNpuPaths
{
    public static string ModelDir => AppPaths.ParakeetNpuDir;

    public static bool IsModelInstalled
    {
        get
        {
            var manifest = ManifestLoader.LoadModelManifest();
            return manifest.Files.All(file =>
            {
                var path = Path.Combine(ModelDir, file.Path);
                if (!File.Exists(path))
                {
                    return false;
                }

                try
                {
                    AssetVerifier.VerifyFile(path, file.Bytes, file.Sha256);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }
    }
}
