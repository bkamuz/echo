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
            "gigaam" => ModelRegistry.HasCompleteGigaAmBundle(LocalDir),
            "whisper" => File.Exists(Path.Combine(LocalDir, "model.bin"))
                || Directory.EnumerateFiles(LocalDir, "*.bin").Any(),
            _ => Directory.EnumerateFiles(LocalDir).Any(),
        };
    }
}
