using echo.Abstractions.Core;
using echo.Engines.ParakeetNpu;
using Microsoft.Extensions.Logging.Abstractions;

namespace echo.Core.Tests;

[CollectionDefinition(nameof(QnnRuntimeDownloaderTests), DisableParallelization = true)]
public sealed class QnnRuntimeDownloaderCollection;

[Collection(nameof(QnnRuntimeDownloaderTests))]
public class QnnRuntimeDownloaderTests
{
    [Fact]
    public void GetMissingFiles_ReturnsAllRequiredWhenRuntimeDirEmpty()
    {
        var runtimeDir = AppPaths.NpuDir;
        var hadDir = Directory.Exists(runtimeDir);
        var backups = BackupRuntimeDir(runtimeDir);
        try
        {
            if (Directory.Exists(runtimeDir))
            {
                Directory.Delete(runtimeDir, recursive: true);
            }

            var downloader = new QnnRuntimeDownloader(new HttpClient(), NullLogger<QnnRuntimeDownloader>.Instance);
            var missing = downloader.GetMissingFiles();

            Assert.Contains("onnxruntime.dll", missing);
            Assert.Contains("onnxruntime_providers_qnn.dll", missing);
            Assert.True(missing.Count >= 9);
        }
        finally
        {
            RestoreRuntimeDir(runtimeDir, hadDir, backups);
        }
    }

    [Fact]
    public void RuntimeManifest_ListsRequiredRuntimeFiles()
    {
        var manifest = ManifestLoader.LoadRuntimeManifest();
        Assert.NotEmpty(manifest.RequiredFiles);
        Assert.Contains("onnxruntime.dll", manifest.RequiredFiles);
    }

    [Fact]
    public void RuntimeManifest_IncludesOnnxRuntimeDllMapping()
    {
        var manifest = ManifestLoader.LoadRuntimeManifest();
        var ortPackage = manifest.Packages.Single(p => p.Name == "onnxruntime");

        Assert.Contains(
            ortPackage.Files,
            mapping => mapping.Target == "onnxruntime.dll"
                && mapping.Source == "onnxruntime/capi/onnxruntime.dll");
    }

    private static Dictionary<string, byte[]> BackupRuntimeDir(string runtimeDir)
    {
        var backups = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(runtimeDir))
        {
            return backups;
        }

        foreach (var file in Directory.EnumerateFiles(runtimeDir, "*", SearchOption.AllDirectories))
        {
            backups[file] = File.ReadAllBytes(file);
        }

        return backups;
    }

    private static void RestoreRuntimeDir(string runtimeDir, bool hadDir, Dictionary<string, byte[]> backups)
    {
        if (Directory.Exists(runtimeDir))
        {
            Directory.Delete(runtimeDir, recursive: true);
        }

        if (!hadDir)
        {
            return;
        }

        Directory.CreateDirectory(runtimeDir);
        foreach (var (path, bytes) in backups)
        {
            var relative = Path.GetRelativePath(runtimeDir, path);
            var target = Path.Combine(runtimeDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }
    }
}
