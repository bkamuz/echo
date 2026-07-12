using System.Text.Json;
using echo.Abstractions.Core;
using Microsoft.Extensions.Logging;

namespace echo.Core.Update;

public sealed class GitHubUpdateChecker : IUpdateChecker
{
    private readonly HttpClient _http;
    private readonly ConfigStore _configStore;
    private readonly ILogger<GitHubUpdateChecker> _logger;

    public GitHubUpdateChecker(
        HttpClient http,
        ConfigStore configStore,
        ILogger<GitHubUpdateChecker> logger)
    {
        _http = http;
        _configStore = configStore;
        _logger = logger;
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!UpdateEnvironment.IsPublishedBuild)
        {
            _logger.LogDebug("Skipping update check: not a published Windows build");
            return UpdateCheckResult.Skipped;
        }

        var config = _configStore.Load();
        if (!forceRefresh && !UpdateEnvironment.ShouldQueryRemote(config.LastUpdateCheckUtc))
        {
            _logger.LogDebug("Skipping remote update check; using cached pending update if any");
            var cached = UpdateEnvironment.TryCreatePendingUpdate(config);
            return cached is null ? UpdateCheckResult.UpToDate : UpdateCheckResult.Available(cached);
        }

        try
        {
            using var response = await _http
                .GetAsync(UpdateEnvironment.UpdateManifestUrl, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Update check failed with HTTP {StatusCode} from {Url}",
                    (int)response.StatusCode,
                    UpdateEnvironment.UpdateManifestUrl);
                return HandleCheckFailure(config);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var update = UpdateManifestParser.TryParseManifest(json, UpdateEnvironment.CurrentVersion);
            PersistCheckResult(config, update);
            return update is null ? UpdateCheckResult.UpToDate : UpdateCheckResult.Available(update);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Update check failed");
            return HandleCheckFailure(config);
        }
    }

    private UpdateCheckResult HandleCheckFailure(AppConfig config)
    {
        config.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
        _configStore.Save(config);

        var cached = UpdateEnvironment.TryCreatePendingUpdate(config);
        return cached is null ? UpdateCheckResult.Failed : UpdateCheckResult.Available(cached);
    }

    private void PersistCheckResult(AppConfig config, UpdateInfo? update)
    {
        config.LastUpdateCheckUtc = DateTimeOffset.UtcNow;

        if (update is null)
        {
            config.PendingUpdateVersion = null;
            config.PendingUpdateDownloadUrl = null;
            config.PendingUpdateReleaseNotesUrl = null;
        }
        else
        {
            config.PendingUpdateVersion = update.Version.ToString();
            config.PendingUpdateDownloadUrl = update.DownloadUrl;
            config.PendingUpdateReleaseNotesUrl = update.ReleaseNotesUrl;
        }

        _configStore.Save(config);
    }
}
