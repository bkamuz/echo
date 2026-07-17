using echo.Abstractions.Core;
using echo.Abstractions.Engines;
using Microsoft.Extensions.Logging;

namespace echo.Engines.Whisper;

public sealed class WhisperModelSupport(ILogger<WhisperModelSupport> logger) : IWhisperModelSupport
{
    public async Task DownloadAsync(
        string modelSize,
        IProgress<string>? progress,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(ProgressMessages.Downloading($"Whisper {modelSize}"));
        logger.LogInformation("Downloading Whisper ggml model {Size}", modelSize);
        await WhisperGgmlHelper.DownloadGgmlModelAsync(modelSize, logger, cancellationToken);
        progress?.Report(ProgressMessages.Done($"Whisper {modelSize}"));
    }

    public void Delete(string modelSize)
    {
        var dir = echo.Abstractions.Core.AppPaths.WhisperDir(modelSize);
        if (!Directory.Exists(dir))
        {
            return;
        }

        Directory.Delete(dir, recursive: true);
        logger.LogInformation("Deleted Whisper model weights: {Size}", modelSize);
    }
}
