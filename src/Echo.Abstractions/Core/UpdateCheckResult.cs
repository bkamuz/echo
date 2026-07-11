namespace echo.Abstractions.Core;

public sealed record UpdateCheckResult(
    UpdateInfo? Update,
    bool CheckFailed,
    bool WasSkipped)
{
    public static UpdateCheckResult Skipped { get; } = new(null, false, true);

    public static UpdateCheckResult UpToDate { get; } = new(null, false, false);

    public static UpdateCheckResult Failed { get; } = new(null, true, false);

    public static UpdateCheckResult Available(UpdateInfo update) => new(update, false, false);
}
