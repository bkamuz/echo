using echo.Core.Update;

namespace echo.Core.Tests;

public class UpdateManifestParserTests
{
    private const string SampleManifestJson = """
        {
          "version": "1.3.4",
          "downloadUrl": "https://github.com/bkamuz/echo/releases/download/v1.3.4/Echo-1.3.4-win-x64-portable.zip",
          "releaseNotesUrl": "https://github.com/bkamuz/echo/releases/tag/v1.3.4"
        }
        """;

    [Fact]
    public void TryParseManifest_ReturnsUpdate_WhenNewerVersionExists()
    {
        var update = UpdateManifestParser.TryParseManifest(SampleManifestJson, new Version(1, 3, 3));

        Assert.NotNull(update);
        Assert.Equal(new Version(1, 3, 4), update.Version);
        Assert.Equal(
            "https://github.com/bkamuz/echo/releases/download/v1.3.4/Echo-1.3.4-win-x64-portable.zip",
            update.DownloadUrl);
        Assert.Equal("https://github.com/bkamuz/echo/releases/tag/v1.3.4", update.ReleaseNotesUrl);
    }

    [Fact]
    public void TryParseManifest_ReturnsNull_WhenVersionIsCurrent()
    {
        var update = UpdateManifestParser.TryParseManifest(SampleManifestJson, new Version(1, 3, 4));
        Assert.Null(update);
    }

    [Fact]
    public void TryParseManifest_ReturnsNull_WhenVersionMatchesWithRevision()
    {
        var update = UpdateManifestParser.TryParseManifest(SampleManifestJson, new Version(1, 3, 4, 0));
        Assert.Null(update);
    }

    [Fact]
    public void TryParseManifest_ReturnsNull_WhenDownloadUrlIsNotHttps()
    {
        const string json = """
            {
              "version": "9.9.9",
              "downloadUrl": "http://example.com/update.zip"
            }
            """;

        var update = UpdateManifestParser.TryParseManifest(json, new Version(1, 0, 0));
        Assert.Null(update);
    }

    [Fact]
    public void TryParseManifest_ReturnsNull_WhenVersionMissing()
    {
        const string json = """
            {
              "downloadUrl": "https://example.com/update.zip"
            }
            """;

        var update = UpdateManifestParser.TryParseManifest(json, new Version(1, 0, 0));
        Assert.Null(update);
    }
}
