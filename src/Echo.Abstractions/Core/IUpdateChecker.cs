namespace echo.Abstractions.Core;

public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckForUpdateAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}
