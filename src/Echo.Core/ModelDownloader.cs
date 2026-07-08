using echo.Abstractions.Core;
using echo.Engines.Whisper;
using Microsoft.Extensions.Logging;

namespace echo.Core;

public sealed class ModelDownloader
{
    private readonly ILogger<ModelDownloader> _logger;
    private readonly HttpClient _http;

    public ModelDownloader(ILogger<ModelDownloader> logger, HttpClient http)
    {
        _logger = logger;
        _http = http;
    }

    public async Task DownloadAsync(ModelSpec spec, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (spec.Engine == "whisper")
        {
            var size = ModelRegistry.WhisperSizeFromSpecId(spec.Id);
            progress?.Report($"Скачивание Whisper {size}…");
            _logger.LogInformation("Downloading Whisper ggml model {Size}", size);
            await WhisperGgmlHelper.DownloadGgmlModelAsync(size, _logger, cancellationToken);
            progress?.Report($"Готово: Whisper {size}");
            return;
        }

        progress?.Report($"Скачивание {spec.Title}…");
        _logger.LogInformation("Downloading {Title} from {Repo}", spec.Title, spec.RepoId);

        Directory.CreateDirectory(spec.LocalDir);
        await DownloadHuggingFaceFolderAsync(spec.RepoId, spec.LocalDir, spec.AllowPatterns, progress, cancellationToken);

        progress?.Report($"Готово: {spec.Title}");
    }

    public void Delete(ModelSpec spec)
    {
        if (spec.Engine == "whisper")
        {
            var size = ModelRegistry.WhisperSizeFromSpecId(spec.Id);
            var dir = AppPaths.WhisperDir(size);
            if (!Directory.Exists(dir))
            {
                return;
            }

            Directory.Delete(dir, recursive: true);
            _logger.LogInformation("Deleted Whisper model weights: {Size}", size);
            return;
        }

        if (!Directory.Exists(spec.LocalDir))
        {
            return;
        }

        Directory.Delete(spec.LocalDir, recursive: true);
        _logger.LogInformation("Deleted model weights: {Title}", spec.Title);
    }

    private async Task DownloadHuggingFaceFolderAsync(
        string repoId,
        string localDir,
        IReadOnlyList<string>? allowPatterns,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var apiUrl = $"https://huggingface.co/api/models/{repoId}/tree/main?recursive=1";
        using var response = await _http.GetAsync(apiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.GetProperty("type").GetString() != "file")
            {
                continue;
            }

            var path = entry.GetProperty("path").GetString();
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (allowPatterns is not null && !MatchesAnyPattern(path, allowPatterns))
            {
                continue;
            }

            var targetPath = Path.Combine(localDir, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            var tmpPath = targetPath + ".tmp";
            if (File.Exists(targetPath))
            {
                continue;
            }

            progress?.Report($"Скачивание {path}…");
            var fileUrl = $"https://huggingface.co/{repoId}/resolve/main/{path}";

            var attempt = 0;
            const int maxRetries = 3;
            while (true)
            {
                attempt++;
                try
                {
                    using var fileResponse = await _http.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    fileResponse.EnsureSuccessStatusCode();
                    await using var stream = await fileResponse.Content.ReadAsStreamAsync(cancellationToken);
                    await using var file = File.Create(tmpPath);
                    await stream.CopyToAsync(file, cancellationToken);
                    break;
                }
                catch when (attempt < maxRetries)
                {
                    await Task.Delay(1000 * attempt, cancellationToken);
                }
            }

            File.Move(tmpPath, targetPath, overwrite: true);
        }
    }

    private static bool MatchesAnyPattern(string path, IReadOnlyList<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\?", ".") + "$";
            if (System.Text.RegularExpressions.Regex.IsMatch(path, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
