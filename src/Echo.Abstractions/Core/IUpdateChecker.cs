namespace echo.Abstractions.Core;

public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckForUpdateAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-fetches latest.json and returns the channel update to apply. Never falls back to
    /// a cached pending_update_* entry — apply must use the current download URL.
    /// </summary>
    Task<UpdateCheckResult> ResolveUpdateForApplyAsync(CancellationToken cancellationToken = default);
}
