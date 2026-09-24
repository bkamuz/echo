using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace echo.Engines.ParakeetNpu;

internal static class QnnWheelExtractor
{
    internal static async Task ExtractMappedFilesAsync(
        ZipArchive archive,
        string runtimeDir,
        IReadOnlyList<QnnRuntimeFileMapping> mappings,
        string wheelName,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        foreach (var mapping in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entryPath = mapping.Source.Replace('\\', '/');
            var entry = archive.GetEntry(entryPath)
                ?? throw new InvalidOperationException($"Wheel {wheelName} missing entry {mapping.Source}");

            var target = Path.Combine(runtimeDir, mapping.Target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var tmpPath = target + ".tmp";

            if (File.Exists(tmpPath))
            {
                File.Delete(tmpPath);
            }

            try
            {
                await using (var entryStream = entry.Open())
                await using (var fileStream = new FileStream(
                                 tmpPath,
                                 FileMode.Create,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 81920,
                                 options: FileOptions.Asynchronous))
                {
                    await entryStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                }

                File.Move(tmpPath, target, overwrite: true);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to extract {Target} from wheel {Wheel} into {Dir}",
                    mapping.Target,
                    wheelName,
                    runtimeDir);
                throw new InvalidOperationException(
                    $"Failed to extract {mapping.Target} from wheel {wheelName}.",
                    ex);
            }

            logger.LogInformation(
                "Installed QNN runtime file {File} ({Bytes} bytes) to {Dir}",
                mapping.Target,
                new FileInfo(target).Length,
                runtimeDir);
        }
    }
}
