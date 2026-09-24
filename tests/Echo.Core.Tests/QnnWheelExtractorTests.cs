using System.IO.Compression;
using echo.Abstractions.Core;
using echo.Engines.ParakeetNpu;
using Microsoft.Extensions.Logging.Abstractions;

namespace echo.Core.Tests;

[Collection(nameof(QnnRuntimeDownloaderTests))]
public class QnnWheelExtractorTests
{
    [Fact]
    public async Task ExtractMappedFilesAsync_WritesTargetsFromFakeWheel()
    {
        var runtimeDir = Path.Combine(Path.GetTempPath(), "echo-qnn-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runtimeDir);

        try
        {
            var payload = "fake-onnxruntime-dll"u8.ToArray();
            await using var wheelStream = BuildFakeWheel(
                ("onnxruntime/capi/onnxruntime.dll", payload),
                ("onnxruntime_qnn/onnxruntime_providers_qnn.dll", "fake-qnn-provider"u8.ToArray()));

            using var archive = new ZipArchive(wheelStream, ZipArchiveMode.Read, leaveOpen: true);
            var mappings = new List<QnnRuntimeFileMapping>
            {
                new() { Source = "onnxruntime/capi/onnxruntime.dll", Target = "onnxruntime.dll" },
                new() { Source = "onnxruntime_qnn/onnxruntime_providers_qnn.dll", Target = "onnxruntime_providers_qnn.dll" },
            };

            await QnnWheelExtractor.ExtractMappedFilesAsync(
                archive,
                runtimeDir,
                mappings,
                "fake-test.whl",
                NullLogger.Instance,
                CancellationToken.None);

            var ortPath = Path.Combine(runtimeDir, "onnxruntime.dll");
            var qnnPath = Path.Combine(runtimeDir, "onnxruntime_providers_qnn.dll");
            Assert.True(File.Exists(ortPath));
            Assert.True(File.Exists(qnnPath));
            Assert.Equal(payload, await File.ReadAllBytesAsync(ortPath));
            Assert.False(File.Exists(ortPath + ".tmp"));
            Assert.False(File.Exists(qnnPath + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(runtimeDir))
            {
                Directory.Delete(runtimeDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExtractMappedFilesAsync_InstallsManifestMappedOrtDll()
    {
        var runtimeDir = AppPaths.NpuDir;
        var hadDir = Directory.Exists(runtimeDir);
        var backups = BackupRuntimeDir(runtimeDir);

        var payload = "stub-ort-runtime"u8.ToArray();
        await using var wheelStream = BuildFakeWheel(("onnxruntime/capi/onnxruntime.dll", payload));
        var manifest = ManifestLoader.LoadRuntimeManifest();
        var ortPackage = manifest.Packages.Single(p => p.Name == "onnxruntime");

        try
        {
            if (Directory.Exists(runtimeDir))
            {
                Directory.Delete(runtimeDir, recursive: true);
            }

            Directory.CreateDirectory(runtimeDir);
            using var archive = new ZipArchive(wheelStream, ZipArchiveMode.Read, leaveOpen: true);
            await QnnWheelExtractor.ExtractMappedFilesAsync(
                archive,
                runtimeDir,
                ortPackage.Files,
                ortPackage.Wheel,
                NullLogger.Instance,
                CancellationToken.None);

            var ortPath = Path.Combine(runtimeDir, "onnxruntime.dll");
            Assert.True(QnnRuntimePaths.IsFilePresent(ortPath));
            Assert.Equal(payload, await File.ReadAllBytesAsync(ortPath));
            Assert.DoesNotContain("onnxruntime.dll", QnnRuntimePaths.GetMissingFiles());
        }
        finally
        {
            RestoreRuntimeDir(runtimeDir, hadDir, backups);
        }
    }

    private static MemoryStream BuildFakeWheel(params (string Entry, byte[] Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entry, content) in entries)
            {
                var zipEntry = archive.CreateEntry(entry, CompressionLevel.Fastest);
                using var entryStream = zipEntry.Open();
                entryStream.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
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
