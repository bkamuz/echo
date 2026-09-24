using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using echo.Abstractions.Core;
using Microsoft.Extensions.Logging;

namespace echo.Engines.ParakeetNpu;

/// <summary>
/// Downloads ORT + QNN plugin DLLs from hash-pinned PyPI wheels into %APPDATA%\Echo\qnn\.
/// </summary>
public sealed class QnnRuntimeDownloader
{
    private const int MaxDownloadRetries = 3;

    private readonly HttpClient _http;
    private readonly ILogger<QnnRuntimeDownloader> _logger;
    private readonly SemaphoreSlim _installLock = new(1, 1);

    public QnnRuntimeDownloader(HttpClient http, ILogger<QnnRuntimeDownloader> logger)
    {
        _http = http;
        _logger = logger;
    }

    public static string RuntimeDir => AppPaths.NpuDir;

    public bool IsInstalled => QnnRuntimePaths.IsInstalled;

    public IReadOnlyList<string> GetMissingFiles() => QnnRuntimePaths.GetMissingFiles();

    public async Task EnsureInstalledAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _installLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInstalledCoreAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _installLock.Release();
        }
    }

    private async Task EnsureInstalledCoreAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var manifest = ManifestLoader.LoadRuntimeManifest();
        if (manifest.RequiredFiles.Count == 0)
        {
            throw new InvalidOperationException(
                "QNN runtime manifest is invalid: required_files is empty.");
        }

        Directory.CreateDirectory(RuntimeDir);

        var missingFiles = QnnRuntimePaths.GetMissingFiles();
        if (missingFiles.Count == 0)
        {
            _logger.LogDebug("QNN runtime already present in {Dir}", RuntimeDir);
            QnnRuntimePaths.PrepareNativeSearchPath();
            return;
        }

        _logger.LogInformation(
            "QNN runtime incomplete in {Dir} ({MissingCount} file(s) missing: {MissingFiles}); downloading required wheels.",
            RuntimeDir,
            missingFiles.Count,
            string.Join(", ", missingFiles));

        foreach (var package in manifest.Packages)
        {
            var neededMappings = package.Files
                .Where(mapping => missingFiles.Contains(mapping.Target))
                .ToList();
            if (neededMappings.Count == 0)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(ProgressMessages.Downloading(package.Wheel));
            _logger.LogInformation("Downloading QNN runtime wheel {Wheel} from {Url}", package.Wheel, package.Url);

            try
            {
                await using var wheelStream = await DownloadWheelAsync(package.Url, cancellationToken)
                    .ConfigureAwait(false);
                await AssetVerifier.VerifyWheelSha256Async(wheelStream, package.Sha256, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "Downloaded QNN runtime wheel {Wheel} ({Bytes} bytes); extracting {FileCount} file(s).",
                    package.Wheel,
                    wheelStream.Length,
                    neededMappings.Count);

                using var archive = new ZipArchive(wheelStream, ZipArchiveMode.Read, leaveOpen: true);
                await QnnWheelExtractor.ExtractMappedFilesAsync(
                    archive,
                    RuntimeDir,
                    neededMappings,
                    package.Wheel,
                    _logger,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "QNN runtime install failed for wheel {Wheel} from {Url}",
                    package.Wheel,
                    package.Url);
                throw;
            }
        }

        WriteRuntimeReceipt(manifest);
        QnnRuntimePaths.PrepareNativeSearchPath();

        var stillMissing = manifest.RequiredFiles
            .Where(file => !QnnRuntimePaths.IsFilePresent(Path.Combine(RuntimeDir, file)))
            .ToList();
        if (stillMissing.Count > 0)
        {
            var message =
                "QNN runtime extraction finished but required files are still missing: "
                + string.Join(", ", stillMissing);
            _logger.LogError("{Message}", message);
            throw new InvalidOperationException(message);
        }

        progress?.Report(ProgressMessages.Done("QNN runtime"));
        _logger.LogInformation("QNN runtime installed to {Dir}", RuntimeDir);
    }

    private async Task<MemoryStream> DownloadWheelAsync(string url, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxDownloadRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var memory = new MemoryStream();
                await response.Content.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
                if (memory.Length == 0)
                {
                    throw new InvalidOperationException("Downloaded wheel is empty.");
                }

                memory.Position = 0;
                return memory;
            }
            catch (Exception ex) when (attempt < MaxDownloadRetries)
            {
                lastError = ex;
                _logger.LogWarning(ex, "QNN wheel download attempt {Attempt}/{Max} failed for {Url}", attempt, MaxDownloadRetries, url);
                await Task.Delay(1000 * attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException($"Failed to download QNN wheel from {url}.", lastError);
    }

    private static void WriteRuntimeReceipt(QnnRuntimeManifest manifest)
    {
        var receipt = new
        {
            schema_version = manifest.SchemaVersion,
            architecture = manifest.Architecture,
            packages = manifest.Packages.Select(p => new { p.Name, p.Version }).ToArray(),
            files = manifest.RequiredFiles.Select(path => new
            {
                path,
                bytes = new FileInfo(Path.Combine(RuntimeDir, path)).Length,
                sha256 = AssetVerifier.ComputeSha256Hex(Path.Combine(RuntimeDir, path)),
            }).ToArray(),
            installed_utc = DateTimeOffset.UtcNow,
        };
        var json = JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(RuntimeDir, "runtime-versions.json"), json);
    }
}

internal static class QnnRuntimePaths
{
    public static string UserDir => AppPaths.NpuDir;

    public static bool IsInstalled => GetMissingFiles().Count == 0;

    public static IReadOnlyList<string> GetMissingFiles()
    {
        var manifest = ManifestLoader.LoadRuntimeManifest();
        return manifest.RequiredFiles
            .Where(file => !IsFilePresent(Path.Combine(UserDir, file)))
            .ToList();
    }

    internal static bool IsFilePresent(string path) =>
        File.Exists(path) && new FileInfo(path).Length > 0;

    public static void PrepareNativeSearchPath(ILogger? logger = null)
    {
        if (!Directory.Exists(UserDir))
        {
            return;
        }

        QnnNativeLoader.PrepareSearchPath(UserDir, logger);

        var ortDll = Path.Combine(UserDir, "onnxruntime.dll");
        if (!IsFilePresent(ortDll))
        {
            return;
        }

        foreach (var name in new[]
                 {
                     "QnnSystem.dll",
                     "QnnHtpPrepare.dll",
                     "QnnHtpNetRunExtensions.dll",
                     "QnnHtp.dll",
                     "onnxruntime.dll",
                 })
        {
            var path = Path.Combine(UserDir, name);
            if (!IsFilePresent(path))
            {
                continue;
            }

            try
            {
                QnnNativeLoader.LoadLibrary(path, logger);
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
            {
                logger?.LogWarning(
                    ex,
                    "Best-effort preload skipped for {LibraryName} from {Path}",
                    name,
                    path);
            }
        }
    }
}
