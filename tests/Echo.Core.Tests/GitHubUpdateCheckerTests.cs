using System.Net;
using echo.Abstractions.Core;
using echo.Core;
using echo.Core.Update;
using Microsoft.Extensions.Logging.Abstractions;

namespace echo.Core.Tests;

public class GitHubUpdateCheckerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string? _previousAppData;
    private readonly string? _previousXdg;

    public GitHubUpdateCheckerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "echo-update-check-" + Guid.NewGuid().ToString("N"));
        _previousAppData = Environment.GetEnvironmentVariable("APPDATA");
        _previousXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Environment.SetEnvironmentVariable("APPDATA", _tempRoot);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _tempRoot);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("APPDATA", _previousAppData);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _previousXdg);
        UpdateEnvironment.ResetTestOverrides();
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task ResolveUpdateForApplyAsync_UsesChannelUrl_NotStalePending()
    {
        const string manifest = """
            {
              "version": "1.9.6",
              "downloadUrl": "https://github.com/bkamuz/echo/releases/download/v1.9.6/Echo-1.9.6-win-x64-portable.zip",
              "downloadUrlWinArm64": "https://github.com/bkamuz/echo/releases/download/v1.9.6/Echo-1.9.6-win-arm64-portable.zip"
            }
            """;

        UpdateEnvironment.CurrentVersionTestOverride = new Version(1, 9, 5);
        UpdateEnvironment.IsPublishedBuildTestOverride = true;
        UpdateEnvironment.IsWindowsArm64ProcessTestOverride = true;

        SeedConfig(new AppConfig
        {
            PendingUpdateVersion = "1.9.5",
            PendingUpdateDownloadUrl =
                "https://github.com/bkamuz/echo/releases/download/v1.9.5/Echo-1.9.5-win-arm64-portable.zip",
        });

        var checker = CreateChecker(manifest);
        var result = await checker.ResolveUpdateForApplyAsync();

        Assert.False(result.CheckFailed);
        Assert.NotNull(result.Update);
        Assert.Equal(new Version(1, 9, 6), result.Update!.Version);
        Assert.Contains("v1.9.6", result.Update.DownloadUrl, StringComparison.OrdinalIgnoreCase);

        var saved = new ConfigStore(NullLogger<ConfigStore>.Instance).Load();
        Assert.Equal("1.9.6", saved.PendingUpdateVersion);
        Assert.Equal(result.Update.DownloadUrl, saved.PendingUpdateDownloadUrl);
    }

    [Fact]
    public async Task ResolveUpdateForApplyAsync_DoesNotFallbackToStalePending_WhenRemoteFails()
    {
        UpdateEnvironment.CurrentVersionTestOverride = new Version(1, 9, 5);
        UpdateEnvironment.IsPublishedBuildTestOverride = true;

        SeedConfig(new AppConfig
        {
            PendingUpdateVersion = "1.9.5",
            PendingUpdateDownloadUrl =
                "https://github.com/bkamuz/echo/releases/download/v1.9.5/Echo-1.9.5-win-arm64-portable.zip",
        });

        var checker = CreateChecker(responseFactory: _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var result = await checker.ResolveUpdateForApplyAsync();

        Assert.True(result.CheckFailed);
        Assert.Null(result.Update);
    }

    [Fact]
    public async Task CheckForUpdateAsync_ClearsPending_WhenChannelIsUpToDate()
    {
        const string manifest = """
            {
              "version": "1.9.5",
              "downloadUrl": "https://github.com/bkamuz/echo/releases/download/v1.9.5/Echo-1.9.5-win-x64-portable.zip",
              "downloadUrlWinArm64": "https://github.com/bkamuz/echo/releases/download/v1.9.5/Echo-1.9.5-win-arm64-portable.zip"
            }
            """;

        UpdateEnvironment.CurrentVersionTestOverride = new Version(1, 9, 5);
        UpdateEnvironment.IsPublishedBuildTestOverride = true;
        UpdateEnvironment.IsWindowsArm64ProcessTestOverride = true;

        SeedConfig(new AppConfig
        {
            PendingUpdateVersion = "1.9.6",
            PendingUpdateDownloadUrl =
                "https://github.com/bkamuz/echo/releases/download/v1.9.6/Echo-1.9.6-win-arm64-portable.zip",
        });

        var checker = CreateChecker(manifest);
        var result = await checker.CheckForUpdateAsync(forceRefresh: true);

        Assert.False(result.CheckFailed);
        Assert.Null(result.Update);

        var saved = new ConfigStore(NullLogger<ConfigStore>.Instance).Load();
        Assert.Null(saved.PendingUpdateVersion);
        Assert.Null(saved.PendingUpdateDownloadUrl);
    }

    [Fact]
    public void TryCreatePendingUpdate_ReturnsNull_WhenUrlVersionMismatchesPendingVersion()
    {
        UpdateEnvironment.CurrentVersionTestOverride = new Version(1, 9, 4);

        var config = new AppConfig
        {
            PendingUpdateVersion = "1.9.6",
            PendingUpdateDownloadUrl =
                "https://github.com/bkamuz/echo/releases/download/v1.9.5/Echo-1.9.5-win-arm64-portable.zip",
        };

        Assert.Null(UpdateEnvironment.TryCreatePendingUpdate(config));
    }

    private void SeedConfig(AppConfig config)
    {
        new ConfigStore(NullLogger<ConfigStore>.Instance).Save(config);
    }

    private static GitHubUpdateChecker CreateChecker(
        string? manifest = null,
        Func<HttpRequestMessage, HttpResponseMessage>? responseFactory = null)
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (responseFactory is not null)
            {
                return responseFactory(request);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(manifest ?? "{}"),
            };
        });

        return new GitHubUpdateChecker(
            new HttpClient(handler),
            new ConfigStore(NullLogger<ConfigStore>.Instance),
            NullLogger<GitHubUpdateChecker>.Instance);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
