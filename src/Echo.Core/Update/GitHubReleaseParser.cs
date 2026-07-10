using System.Text.Json;
using echo.Abstractions.Core;

namespace echo.Core.Update;

public static class GitHubReleaseParser
{
    public static UpdateInfo? TryParseLatestRelease(string json, Version currentVersion)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("tag_name", out var tagNameElement))
        {
            return null;
        }

        var tagName = tagNameElement.GetString();
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return null;
        }

        if (!TryParseTagVersion(tagName, out var releaseVersion))
        {
            return null;
        }

        if (releaseVersion <= currentVersion)
        {
            return null;
        }

        if (!root.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? downloadUrl = null;
        foreach (var asset in assetsElement.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameElement))
            {
                continue;
            }

            var name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!name.EndsWith(UpdateEnvironment.WindowsPortableAssetSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!asset.TryGetProperty("browser_download_url", out var urlElement))
            {
                continue;
            }

            downloadUrl = urlElement.GetString();
            break;
        }

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return null;
        }

        string? releaseNotesUrl = null;
        if (root.TryGetProperty("html_url", out var htmlUrlElement))
        {
            releaseNotesUrl = htmlUrlElement.GetString();
        }

        return new UpdateInfo
        {
            Version = releaseVersion,
            DownloadUrl = downloadUrl,
            ReleaseNotesUrl = releaseNotesUrl,
        };
    }

    public static bool TryParseTagVersion(string tagName, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return false;
        }

        var trimmed = tagName.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        if (!Version.TryParse(trimmed, out var parsed) || parsed is null)
        {
            return false;
        }

        version = parsed;
        return true;
    }

    public static bool IsNewerVersion(Version available, Version current) => available > current;
}
