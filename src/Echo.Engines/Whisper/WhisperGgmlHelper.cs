using echo.Abstractions.Core;
using Microsoft.Extensions.Logging;
using Whisper.net.Ggml;

namespace echo.Engines.Whisper;

public static class WhisperGgmlHelper
{
    public static GgmlType MapSizeToGgmlType(string size) => size switch
    {
        "tiny" => GgmlType.Tiny,
        "base" => GgmlType.Base,
        "small" => GgmlType.Small,
        "medium" => GgmlType.Medium,
        "large-v3" => GgmlType.LargeV3,
        "large-v3-turbo" => GgmlType.LargeV3Turbo,
        _ => throw new ArgumentException($"Unknown Whisper model size: {size}", nameof(size)),
    };

    public static string ResolveGgmlModelPath(string size)
    {
        var ggmlPath = ModelRegistry.WhisperGgmlPath(size);
        if (File.Exists(ggmlPath))
        {
            return ggmlPath;
        }

        throw new InvalidOperationException(
            $"Whisper {size} модель не найдена. Скачайте через настройки приложения.");
    }

    public static async Task DownloadGgmlModelAsync(
        string size,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var ggmlPath = ModelRegistry.WhisperGgmlPath(size);
        Directory.CreateDirectory(Path.GetDirectoryName(ggmlPath)!);

        if (File.Exists(ggmlPath))
        {
            return;
        }

        var ggmlType = MapSizeToGgmlType(size);
        logger?.LogInformation("Downloading ggml Whisper model {Type}", ggmlType);
        using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(ggmlType, cancellationToken: cancellationToken);
        await using var file = File.Create(ggmlPath);
        await modelStream.CopyToAsync(file, cancellationToken);
    }
}
