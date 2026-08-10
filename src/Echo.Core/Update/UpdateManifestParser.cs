using System.Text.Json;
using echo.Abstractions.Core;

namespace echo.Core.Update;

public static class UpdateManifestParser
{
    public static UpdateInfo? TryParseManifest(string json, Version currentVersion) =>
        TryParseManifest(json, currentVersion, UpdateEnvironment.IsWindowsArm64Process);

    /// <summary>
    /// Parses latest.json. On ARM64 prefers downloadUrlWinArm64; otherwise downloadUrl (x64).
    /// Does not fall back from ARM to x64 — wrong-arch zips must never be offered.
    /// </summary>
    public static UpdateInfo? TryParseManifest(string json, Version currentVersion, bool preferArm64)
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

        var downloadUrl = preferArm64
            ? ReadHttpsUrl(root, "downloadUrlWinArm64")
            : ReadHttpsUrl(root, "downloadUrl");

        if (downloadUrl is null)
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

    private static string? ReadHttpsUrl(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        var url = element.GetString();
        if (string.IsNullOrWhiteSpace(url)
            || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return url;
    }
}
