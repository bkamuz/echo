namespace echo.Abstractions.Core;

public interface IUpdateChecker
{
    Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default);
}
