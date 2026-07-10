namespace echo.Abstractions.Core;

public sealed class UpdateInfo
{
    public required Version Version { get; init; }
    public required string DownloadUrl { get; init; }
    public string? ReleaseNotesUrl { get; init; }
}
