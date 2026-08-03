using echo.Abstractions.Core;
using echo.Abstractions.Engines;
using Microsoft.Extensions.Logging;

namespace echo.Core;

public sealed class ModelDownloader
{
    private readonly ILogger<ModelDownloader> _logger;
    private readonly HttpClient _http;
    private readonly IWhisperModelSupport? _whisper;

    public ModelDownloader(
        ILogger<ModelDownloader> logger,
        HttpClient http,
        IWhisperModelSupport? whisper = null)
    {
        _logger = logger;
        _http = http;
        _whisper = whisper;
    }

    public async Task DownloadAsync(ModelSpec spec, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (spec.Engine == "whisper")
        {
            if (_whisper is null)
            {
                throw new InvalidOperationException(
                    "Whisper не включён в этой сборке Echo.");
            }

            var size = ModelRegistry.WhisperSizeFromSpecId(spec.Id);
            await _whisper.DownloadAsync(size, progress, cancellationToken);
            return;
        }

        progress?.Report(ProgressMessages.Downloading(spec.Title));
        _logger.LogInformation("Downloading {Title} from {Repo}", spec.Title, spec.RepoId);

        Directory.CreateDirectory(spec.LocalDir);
        if (!string.IsNullOrWhiteSpace(spec.GitHubReleaseTag))
        {
            await DownloadGitHubReleaseAsync(
                spec.RepoId,
                spec.GitHubReleaseTag,
                spec.LocalDir,
                spec.AllowPatterns,
                progress,
                cancellationToken);
        }
        else
        {
            await DownloadHuggingFaceFolderAsync(
                spec.RepoId,
                spec.LocalDir,
                spec.AllowPatterns,
                progress,
                cancellationToken);
        }

        progress?.Report(ProgressMessages.Done(spec.Title));
    }

    public void Delete(ModelSpec spec)
    {
        if (spec.Engine == "whisper")
        {
            if (_whisper is null)
            {
                return;
            }

            var size = ModelRegistry.WhisperSizeFromSpecId(spec.Id);
            _whisper.Delete(size);
            return;
        }

        if (!Directory.Exists(spec.LocalDir))
        {
            return;
        }

        Directory.Delete(spec.LocalDir, recursive: true);
        _logger.LogInformation("Deleted model weights: {Title}", spec.Title);
    }

    private async Task DownloadGitHubReleaseAsync(
        string repoId,
        string tag,
        string localDir,
        IReadOnlyList<string>? allowPatterns,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (allowPatterns is null || allowPatterns.Count == 0)
        {
            throw new InvalidOperationException(
                $"GitHub release download for '{repoId}' requires AllowPatterns.");
        }

        foreach (var fileName in allowPatterns)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var targetPath = Path.Combine(localDir, fileName);
            if (File.Exists(targetPath))
            {
                continue;
            }

            progress?.Report(ProgressMessages.Downloading(fileName));
            var fileUrl = $"https://github.com/{repoId}/releases/download/{tag}/{fileName}";
            _logger.LogInformation("Downloading {File} from {Url}", fileName, fileUrl);

            var tmpPath = targetPath + ".tmp";
            var attempt = 0;
            const int maxRetries = 3;
            Exception? lastError = null;
            var optionalFp32 = fileName.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)
                && !fileName.Contains("_int8", StringComparison.OrdinalIgnoreCase);

            while (attempt < maxRetries)
            {
                attempt++;
                try
                {
                    using var fileResponse = await _http.GetAsync(
                        fileUrl,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);
                    if (fileResponse.StatusCode == System.Net.HttpStatusCode.NotFound && optionalFp32)
                    {
                        // Optional fp32 fallback may be omitted from the release.
                        _logger.LogInformation("Optional asset missing, skipping: {File}", fileName);
                        lastError = null;
                        break;
                    }

                    fileResponse.EnsureSuccessStatusCode();
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    await using var stream = await fileResponse.Content.ReadAsStreamAsync(cancellationToken);
                    await using var file = File.Create(tmpPath);
                    await stream.CopyToAsync(file, cancellationToken);
                    File.Move(tmpPath, targetPath, overwrite: true);
                    lastError = null;
                    break;
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    lastError = ex;
                    await Task.Delay(1000 * attempt, cancellationToken);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            if (lastError is not null && !optionalFp32)
            {
                throw new InvalidOperationException(
                    $"Failed to download '{fileName}' from GitHub release '{tag}'.",
                    lastError);
            }
        }
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

            progress?.Report(ProgressMessages.Downloading(path));
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
