using echo.Abstractions.Core;
using Microsoft.Extensions.Logging;

namespace echo.Engines.ParakeetNpu;

public sealed class ParakeetModelDownloader
{
    private const int MaxDownloadRetries = 3;

    private readonly HttpClient _http;
    private readonly ILogger<ParakeetModelDownloader> _logger;

    public ParakeetModelDownloader(HttpClient http, ILogger<ParakeetModelDownloader> logger)
    {
        _http = http;
        _logger = logger;
    }

    public static string ModelDir => AppPaths.ParakeetNpuDir;

    public bool IsInstalled => ParakeetNpuPaths.IsModelInstalled;

    public bool IsCpuEncoderInstalled => ParakeetNpuPaths.IsCpuEncoderInstalled;

    public async Task EnsureCpuEncoderAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var manifest = ManifestLoader.LoadModelManifest();
        var file = manifest.Files.FirstOrDefault(f =>
            string.Equals(f.Path, "encoder-model.int8.onnx", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Parakeet CPU encoder asset is missing from the model manifest.");

        await EnsureFileAsync(file, progress, cancellationToken).ConfigureAwait(false);
    }

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

        foreach (var file in manifest.Files.Where(f =>
                     !string.Equals(f.Path, "encoder-model.int8.onnx", StringComparison.Ordinal)))
        {
            await EnsureFileAsync(file, progress, cancellationToken).ConfigureAwait(false);
        }

        if (!IsInstalled)
        {
            throw new InvalidOperationException("Parakeet NPU model download finished but verification failed.");
        }

        progress?.Report(ProgressMessages.Done("Parakeet NPU model"));
        _logger.LogInformation("Parakeet NPU model installed to {Dir}", ModelDir);
    }

    private async Task EnsureFileAsync(
        ParakeetModelFile file,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = Path.Combine(ModelDir, file.Path);
        if (File.Exists(target))
        {
            try
            {
                AssetVerifier.VerifyFile(target, file.Bytes, file.Sha256);
                return;
            }
            catch (InvalidOperationException)
            {
                File.Delete(target);
            }
        }

        progress?.Report(ProgressMessages.Downloading(file.Path));
        _logger.LogInformation("Downloading Parakeet asset {File} from {Url}", file.Path, file.Url);

        Directory.CreateDirectory(ModelDir);
        var tmp = target + ".tmp";
        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxDownloadRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await _http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var fileStream = File.Create(tmp))
                {
                    await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                }

                if (new FileInfo(tmp).Length == 0)
                {
                    throw new InvalidOperationException($"Downloaded asset '{file.Path}' is empty.");
                }

                File.Move(tmp, target, overwrite: true);
                AssetVerifier.VerifyFile(target, file.Bytes, file.Sha256);
                return;
            }
            catch (Exception ex) when (attempt < MaxDownloadRetries)
            {
                lastError = ex;
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }

                _logger.LogWarning(
                    ex,
                    "Parakeet asset download attempt {Attempt}/{Max} failed for {File}",
                    attempt,
                    MaxDownloadRetries,
                    file.Path);
                await Task.Delay(1000 * attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
        }

        throw new InvalidOperationException($"Failed to download Parakeet asset '{file.Path}'.", lastError);
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

    public static bool IsCpuEncoderInstalled => IsFileInstalled("encoder-model.int8.onnx");

    public static bool IsModelInstalled
    {
        get
        {
            var manifest = ManifestLoader.LoadModelManifest();
            return manifest.Files
                .Where(file => !string.Equals(file.Path, "encoder-model.int8.onnx", StringComparison.Ordinal))
                .All(file => IsFileInstalled(file.Path, file.Bytes, file.Sha256));
        }
    }

    private static bool IsFileInstalled(string relativePath, long? bytes = null, string? sha256 = null)
    {
        var path = Path.Combine(ModelDir, relativePath);
        if (!File.Exists(path))
        {
            return false;
        }

        if (bytes is null || sha256 is null)
        {
            return true;
        }

        try
        {
            AssetVerifier.VerifyFile(path, bytes.Value, sha256);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
