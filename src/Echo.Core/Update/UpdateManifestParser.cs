using System.Text.Json;
using echo.Abstractions.Core;

namespace echo.Core.Update;

public static class UpdateManifestParser
{
    public static UpdateInfo? TryParseManifest(string json, Version currentVersion)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("version", out var versionElement))
        {
            return null;
        }

        var versionText = versionElement.GetString();
        if (string.IsNullOrWhiteSpace(versionText) || !Version.TryParse(versionText, out var releaseVersion))
        {
            return null;
        }

        if (!UpdateEnvironment.IsNewerVersion(releaseVersion, currentVersion))
        {
            return null;
        }

        if (!root.TryGetProperty("downloadUrl", out var downloadUrlElement))
        {
            return null;
        }

        var downloadUrl = downloadUrlElement.GetString();
        if (string.IsNullOrWhiteSpace(downloadUrl)
            || !downloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? releaseNotesUrl = null;
        if (root.TryGetProperty("releaseNotesUrl", out var releaseNotesElement))
        {
            releaseNotesUrl = releaseNotesElement.GetString();
        }

        return new UpdateInfo
        {
            Version = UpdateEnvironment.NormalizeVersion(releaseVersion),
            DownloadUrl = downloadUrl,
            ReleaseNotesUrl = releaseNotesUrl,
        };
    }
}
