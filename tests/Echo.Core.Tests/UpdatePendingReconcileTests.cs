using echo.Core;
using echo.Core.Update;
using echo.Abstractions.Core;

namespace echo.Core.Tests;

public class UpdatePendingReconcileTests
{
    [Fact]
    public void ReconcilePendingUpdate_ReplacesPendingWithChannelUpdate()
    {
        var config = new AppConfig
        {
            PendingUpdateVersion = "1.9.5",
            PendingUpdateDownloadUrl =
                "https://github.com/bkamuz/echo/releases/download/v1.9.5/Echo-1.9.5-win-arm64-portable.zip",
        };

        var channel = new UpdateInfo
        {
            Version = new Version(1, 9, 6),
            DownloadUrl =
                "https://github.com/bkamuz/echo/releases/download/v1.9.6/Echo-1.9.6-win-arm64-portable.zip",
        };

        var reconciled = UpdateEnvironment.ReconcilePendingUpdate(config, channel);

        Assert.NotNull(reconciled);
        Assert.Equal(channel.DownloadUrl, config.PendingUpdateDownloadUrl);
        Assert.Equal("1.9.6", config.PendingUpdateVersion);
    }

    [Theory]
    [InlineData(
        "https://github.com/bkamuz/echo/releases/download/v1.9.6/Echo-1.9.6-win-arm64-portable.zip",
        "1.9.6",
        true)]
    [InlineData(
        "https://github.com/bkamuz/echo/releases/download/v1.9.5/Echo-1.9.5-win-arm64-portable.zip",
        "1.9.6",
        false)]
    public void PendingDownloadUrlMatchesVersion_ValidatesUrl(string url, string version, bool expected)
    {
        var matches = UpdateEnvironment.PendingDownloadUrlMatchesVersion(url, Version.Parse(version));
        Assert.Equal(expected, matches);
    }
}
