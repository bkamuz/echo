using echo.Core.Update;
using echo.Core;

namespace echo.Core.Tests;

public class GitHubReleaseParserTests
{
  private const string SampleReleaseJson = """
    {
      "tag_name": "v1.3.0",
      "html_url": "https://github.com/bkamuz/echo/releases/tag/v1.3.0",
      "assets": [
        {
          "name": "Echo-1.3.0-linux-x64-portable.tar.gz",
          "browser_download_url": "https://github.com/bkamuz/echo/releases/download/v1.3.0/Echo-1.3.0-linux-x64-portable.tar.gz"
        },
        {
          "name": "Echo-1.3.0-win-x64-portable.zip",
          "browser_download_url": "https://github.com/bkamuz/echo/releases/download/v1.3.0/Echo-1.3.0-win-x64-portable.zip"
        }
      ]
    }
    """;

    [Fact]
    public void TryParseLatestRelease_ReturnsUpdate_WhenNewerVersionExists()
    {
        var update = GitHubReleaseParser.TryParseLatestRelease(SampleReleaseJson, new Version(1, 2, 0));

        Assert.NotNull(update);
        Assert.Equal(new Version(1, 3, 0), update.Version);
        Assert.Equal(
            "https://github.com/bkamuz/echo/releases/download/v1.3.0/Echo-1.3.0-win-x64-portable.zip",
            update.DownloadUrl);
        Assert.Equal("https://github.com/bkamuz/echo/releases/tag/v1.3.0", update.ReleaseNotesUrl);
    }

    [Fact]
    public void TryParseLatestRelease_ReturnsNull_WhenVersionIsCurrent()
    {
        var update = GitHubReleaseParser.TryParseLatestRelease(SampleReleaseJson, new Version(1, 3, 0));
        Assert.Null(update);
    }

    [Theory]
    [InlineData("v1.3.0", "1.3.0")]
    [InlineData("1.3.0", "1.3.0")]
    public void TryParseTagVersion_ParsesReleaseTags(string tagName, string expected)
    {
        Assert.True(GitHubReleaseParser.TryParseTagVersion(tagName, out var version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Fact]
    public void IsNewerVersion_ComparesSemver()
    {
        Assert.True(GitHubReleaseParser.IsNewerVersion(new Version(1, 3, 0), new Version(1, 2, 0)));
        Assert.False(GitHubReleaseParser.IsNewerVersion(new Version(1, 2, 0), new Version(1, 2, 0)));
        Assert.False(GitHubReleaseParser.IsNewerVersion(new Version(1, 2, 0), new Version(1, 3, 0)));
    }

    [Fact]
    public void TryCreatePendingUpdate_ReturnsCachedUpdate_WhenStillNewer()
    {
        var config = new AppConfig
        {
            PendingUpdateVersion = "1.3.0",
            PendingUpdateDownloadUrl = "https://example.com/update.zip",
            PendingUpdateReleaseNotesUrl = "https://example.com/notes",
        };

        var currentVersion = UpdateEnvironment.CurrentVersion;
        if (currentVersion >= new Version(1, 3, 0))
        {
            return;
        }

        var update = UpdateEnvironment.TryCreatePendingUpdate(config);

        Assert.NotNull(update);
        Assert.Equal(new Version(1, 3, 0), update.Version);
        Assert.Equal("https://example.com/update.zip", update.DownloadUrl);
    }
}
