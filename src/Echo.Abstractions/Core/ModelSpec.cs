namespace echo.Abstractions.Core;

public sealed record ModelSpec(
    string Id,
    string Title,
    string Engine,
    string RepoId,
    string LocalDir,
    IReadOnlyList<string>? AllowPatterns = null)
{
    public bool IsDownloaded()
    {
        if (!Directory.Exists(LocalDir))
        {
            return false;
        }

        return Engine switch
        {
            "gigaam" => ModelRegistry.ResolveGigaAmBundle(
                LocalDir,
                ModelRegistry.GigaAmVariantFromSpecId(Id)) is not null,
            "whisper" => ModelRegistry.IsWhisperDownloaded(ModelRegistry.WhisperSizeFromSpecId(Id)),
            _ => Directory.EnumerateFiles(LocalDir).Any(),
        };
    }
}
