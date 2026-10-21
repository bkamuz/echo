using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using echo.Abstractions.Core;
using Microsoft.Extensions.Logging;

namespace echo.Engines.ParakeetNpu.Whisper;

/// <summary>
/// Downloads the Qualcomm AI Hub Whisper-Large-V3-Turbo PRECOMPILED_QNN_ONNX build for the local
/// Snapdragon (X2 Elite → V81 context, X Elite → V73 context) from Qualcomm S3 (Apache-2.0),
/// plus the OpenAI Whisper vocab (MIT) from Hugging Face. Nothing is bundled in the app zip.
/// </summary>
public sealed class WhisperNpuModelDownloader
{
    public const string ReleaseAssetsUrl =
        "https://huggingface.co/qualcomm/Whisper-Large-V3-Turbo/resolve/main/release_assets.json";

    public const string PinnedReleaseVersion = "0.64.0";

    public const string VocabUrl =
        "https://huggingface.co/openai/whisper-large-v3-turbo/resolve/main/vocab.json";

    public const string AddedTokensUrl =
        "https://huggingface.co/openai/whisper-large-v3-turbo/resolve/main/added_tokens.json";

    public const string RequiredQairtPrefix = "2.50";

    private const int MaxDownloadRetries = 4;

    private readonly HttpClient _http;
    private readonly ILogger<WhisperNpuModelDownloader> _logger;

    public WhisperNpuModelDownloader(HttpClient http, ILogger<WhisperNpuModelDownloader> logger)
    {
        _http = http;
        _logger = logger;
    }

    public static string ModelDir => AppPaths.WhisperNpuDir;

    public bool IsInstalled => ModelRegistry.IsWhisperNpuDownloaded(ModelDir) && IsForCurrentChip(ModelDir, HtpHardwareProfile.Resolve());

    public static string ChipsetKey(HtpHardwareProfile profile) =>
        profile.Generation == HexagonHtpGeneration.V81
            ? "qualcomm-snapdragon-x2-elite"
            : "qualcomm-snapdragon-x-elite";

    public static string PinnedUrl(string chipsetKey) =>
        "https://qaihub-public-assets.s3.us-west-2.amazonaws.com/qai-hub-models/models/whisper_large_v3_turbo/releases/"
        + $"v{PinnedReleaseVersion}/whisper_large_v3_turbo-precompiled_qnn_onnx-float-{chipsetKey.Replace('-', '_')}.zip";

    /// <summary>Picks the precompiled_qnn_onnx URL for a chipset from HF release_assets.json.</summary>
    public static string? ParseAssetUrl(string releaseAssetsJson, string chipsetKey)
    {
        using var doc = JsonDocument.Parse(releaseAssetsJson);
        if (doc.RootElement.TryGetProperty("precisions", out var precisions)
            && precisions.TryGetProperty("float", out var fp)
            && fp.TryGetProperty("chipset_assets", out var chips)
            && chips.TryGetProperty(chipsetKey, out var chip)
            && chip.TryGetProperty("precompiled_qnn_onnx", out var asset)
            && asset.TryGetProperty("download_url", out var url))
        {
            if (asset.TryGetProperty("tool_versions", out var tools)
                && tools.TryGetProperty("qairt", out var qairt)
                && !(qairt.GetString() ?? string.Empty).StartsWith(RequiredQairtPrefix, StringComparison.Ordinal))
            {
                // Newer QAIRT context binaries may not load on our pinned onnxruntime-qnn 2.6.0 (QAIRT 2.50).
                return null;
            }

            return url.GetString();
        }

        return null;
    }

    /// <summary>The installed model was compiled for this chip (X2 Elite uses a different HTP context).</summary>
    public static bool IsForCurrentChip(string dir, HtpHardwareProfile profile)
    {
        var marker = Path.Combine(dir, "chipset.txt");
        return File.Exists(marker)
            && string.Equals(File.ReadAllText(marker).Trim(), ChipsetKey(profile), StringComparison.Ordinal);
    }

    public void Delete()
    {
        if (Directory.Exists(ModelDir))
        {
            Directory.Delete(ModelDir, recursive: true);
        }
    }

    public async Task EnsureInstalledAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var profile = HtpHardwareProfile.Resolve();
        var chipset = ChipsetKey(profile);
        Directory.CreateDirectory(ModelDir);

        if (!IsForCurrentChip(ModelDir, profile) || !HasModelFiles())
        {
            var url = await ResolveAssetUrlAsync(chipset, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Whisper NPU: downloading {Chipset} model ({Target}) from {Url}",
                chipset,
                profile.ContextTarget,
                url);
            var zipPath = Path.Combine(ModelDir, "download.zip.part");
            await DownloadResumableAsync(url, zipPath, "Whisper Turbo NPU", progress, cancellationToken).ConfigureAwait(false);
            progress?.Report(ProgressMessages.Saving());
            ExtractModel(zipPath, ModelDir, _logger);
            File.Delete(zipPath);
            await File.WriteAllTextAsync(Path.Combine(ModelDir, "chipset.txt"), chipset, cancellationToken).ConfigureAwait(false);
        }

        await EnsureSmallFileAsync(VocabUrl, "vocab.json", progress, cancellationToken).ConfigureAwait(false);
        await EnsureSmallFileAsync(AddedTokensUrl, "added_tokens.json", progress, cancellationToken).ConfigureAwait(false);

        if (!ModelRegistry.IsWhisperNpuDownloaded(ModelDir))
        {
            throw new InvalidOperationException("Whisper NPU model download finished but files are missing.");
        }

        progress?.Report(ProgressMessages.Done("Whisper Turbo NPU"));
        _logger.LogInformation("Whisper NPU model installed to {Dir} ({Chipset})", ModelDir, chipset);
    }

    private static bool HasModelFiles() =>
        new[] { "encoder.onnx", "encoder.bin", "decoder.onnx", "decoder.bin", "metadata.json" }
            .All(f => File.Exists(Path.Combine(ModelDir, f)));

    private async Task<string> ResolveAssetUrlAsync(string chipset, CancellationToken cancellationToken)
    {
        try
        {
            var json = await _http.GetStringAsync(ReleaseAssetsUrl, cancellationToken).ConfigureAwait(false);
            var url = ParseAssetUrl(json, chipset);
            if (url is not null)
            {
                return url;
            }

            _logger.LogWarning(
                "Whisper NPU: release_assets.json has no QAIRT {Qairt} precompiled_qnn_onnx asset for {Chipset}; using pinned v{Version}",
                RequiredQairtPrefix,
                chipset,
                PinnedReleaseVersion);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Whisper NPU: could not read {Url}; using pinned v{Version}", ReleaseAssetsUrl, PinnedReleaseVersion);
        }

        return PinnedUrl(chipset);
    }

    internal static void ExtractModel(string zipPath, string targetDir, ILogger? logger)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var wanted = new HashSet<string>(StringComparer.Ordinal)
        {
            "encoder.onnx", "encoder.bin", "decoder.onnx", "decoder.bin", "metadata.json",
        };
        foreach (var entry in archive.Entries)
        {
            if (!wanted.Contains(entry.Name))
            {
                continue;
            }

            var target = Path.Combine(targetDir, entry.Name);
            logger?.LogInformation("Whisper NPU: extracting {Entry} ({Bytes} bytes)", entry.FullName, entry.Length);
            entry.ExtractToFile(target + ".tmp", overwrite: true);
            File.Move(target + ".tmp", target, overwrite: true);
            wanted.Remove(entry.Name);
        }

        if (wanted.Count > 0)
        {
            throw new InvalidOperationException($"Whisper NPU archive is missing: {string.Join(", ", wanted)}");
        }
    }

    private async Task EnsureSmallFileAsync(string url, string name, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var target = Path.Combine(ModelDir, name);
        if (File.Exists(target) && new FileInfo(target).Length > 0)
        {
            return;
        }

        progress?.Report(ProgressMessages.Downloading(name));
        var bytes = await _http.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(target + ".tmp", bytes, cancellationToken).ConfigureAwait(false);
        File.Move(target + ".tmp", target, overwrite: true);
    }

    private async Task DownloadResumableAsync(
        string url,
        string partPath,
        string label,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxDownloadRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (existing > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(existing, null);
                }

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    return; // already complete
                }

                response.EnsureSuccessStatusCode();
                var append = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                var total = (append ? existing : 0) + (response.Content.Headers.ContentLength ?? 0);
                _logger.LogInformation(
                    "Whisper NPU: {Mode} download, {Have} of {Total} bytes",
                    append ? "resuming" : "starting",
                    append ? existing : 0,
                    total);

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var file = new FileStream(partPath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
                var buffer = new byte[1 << 20];
                var done = append ? existing : 0;
                var lastPercent = -1;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    done += read;
                    if (total > 0)
                    {
                        var percent = (int)(done * 100 / total);
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            progress?.Report(ProgressMessages.Downloading($"{label} {percent}%"));
                        }
                    }
                }

                if (total > 0 && done < total)
                {
                    throw new IOException($"Download ended early ({done} of {total} bytes).");
                }

                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < MaxDownloadRetries)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Whisper NPU download attempt {Attempt}/{Max} failed; will resume", attempt, MaxDownloadRetries);
                await Task.Delay(2000 * attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException($"Failed to download Whisper NPU model from {url}.", lastError);
    }
}
