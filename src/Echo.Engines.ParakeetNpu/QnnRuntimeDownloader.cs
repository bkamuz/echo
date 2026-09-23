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

            await using var wheelStream = await DownloadWheelAsync(package.Url, cancellationToken).ConfigureAwait(false);
            await AssetVerifier.VerifyWheelSha256Async(wheelStream, package.Sha256, cancellationToken)
                .ConfigureAwait(false);

            using var archive = new ZipArchive(wheelStream, ZipArchiveMode.Read, leaveOpen: true);
            foreach (var mapping in neededMappings)
            {
                var entry = archive.GetEntry(mapping.Source.Replace('\\', '/'))
                    ?? throw new InvalidOperationException($"Wheel {package.Wheel} missing entry {mapping.Source}");
                var target = Path.Combine(RuntimeDir, mapping.Target);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var entryStream = entry.Open();
                await using var fileStream = File.Create(target + ".tmp");
                await entryStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                File.Move(target + ".tmp", target, overwrite: true);
                _logger.LogInformation(
                    "Installed QNN runtime file {File} ({Bytes} bytes) to {Dir}",
                    mapping.Target,
                    new FileInfo(target).Length,
                    RuntimeDir);
            }
        }

        WriteRuntimeReceipt(manifest);
        QnnRuntimePaths.PrepareNativeSearchPath();

        var stillMissing = manifest.RequiredFiles
            .Where(file => !QnnRuntimePaths.IsFilePresent(Path.Combine(RuntimeDir, file)))
            .ToList();
        if (stillMissing.Count > 0)
        {
            throw new InvalidOperationException(
                "QNN runtime extraction finished but required files are still missing: "
                + string.Join(", ", stillMissing));
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

    public static void PrepareNativeSearchPath()
    {
        var dir = UserDir;
        if (!Directory.Exists(dir))
        {
            return;
        }

        var ortDll = Path.Combine(dir, "onnxruntime.dll");
        if (!IsFilePresent(ortDll))
        {
            return;
        }

        Environment.SetEnvironmentVariable("ORT_DYLIB_PATH", ortDll);

        try
        {
            NativeLibrary.Load(Path.Combine(dir, "QnnSystem.dll"));
            NativeLibrary.Load(Path.Combine(dir, "QnnHtpPrepare.dll"));
            NativeLibrary.Load(Path.Combine(dir, "QnnHtpNetRunExtensions.dll"));
            NativeLibrary.Load(Path.Combine(dir, "QnnHtp.dll"));
            NativeLibrary.Load(ortDll);
        }
        catch
        {
            // Best-effort preload; session creation will surface errors.
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (!path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(p => string.Equals(p, dir, StringComparison.OrdinalIgnoreCase)))
        {
            Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + path);
        }
    }
}
