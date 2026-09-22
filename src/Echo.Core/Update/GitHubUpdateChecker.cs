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

    public Task<UpdateCheckResult> ResolveUpdateForApplyAsync(CancellationToken cancellationToken = default) =>
        QueryRemoteAsync(fallbackToCachedPending: false, cancellationToken);

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

        return await QueryRemoteAsync(fallbackToCachedPending: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<UpdateCheckResult> QueryRemoteAsync(
        bool fallbackToCachedPending,
        CancellationToken cancellationToken)
    {
        if (!UpdateEnvironment.IsPublishedBuild)
        {
            return UpdateCheckResult.Skipped;
        }

        var config = _configStore.Load();
        var previousPendingVersion = config.PendingUpdateVersion;

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
                return HandleCheckFailure(config, fallbackToCachedPending);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var update = UpdateManifestParser.TryParseManifest(json, UpdateEnvironment.CurrentVersion);
            PersistCheckResult(config, update);

            if (update is not null
                && !string.IsNullOrWhiteSpace(previousPendingVersion)
                && Version.TryParse(previousPendingVersion, out var previousPending)
                && UpdateEnvironment.IsNewerVersion(update.Version, previousPending))
            {
                _logger.LogInformation(
                    "Replaced stale pending update {OldVersion} with channel version {NewVersion}",
                    previousPendingVersion,
                    update.Version);
            }

            return update is null ? UpdateCheckResult.UpToDate : UpdateCheckResult.Available(update);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Update check failed");
            return HandleCheckFailure(config, fallbackToCachedPending);
        }
    }

    private UpdateCheckResult HandleCheckFailure(AppConfig config, bool fallbackToCachedPending)
    {
        config.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
        _configStore.Save(config);

        if (!fallbackToCachedPending)
        {
            return UpdateCheckResult.Failed;
        }

        var cached = UpdateEnvironment.TryCreatePendingUpdate(config);
        return cached is null ? UpdateCheckResult.Failed : UpdateCheckResult.Available(cached);
    }

    private void PersistCheckResult(AppConfig config, UpdateInfo? update)
    {
        config.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
        UpdateEnvironment.ReconcilePendingUpdate(config, update);
        _configStore.Save(config);
    }
}
