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
            "gigaam" => HasCompleteGigaAmBundle(LocalDir),
            "whisper" => File.Exists(Path.Combine(LocalDir, "model.bin"))
                || Directory.EnumerateFiles(LocalDir, "*.bin").Any(),
            _ => Directory.EnumerateFiles(LocalDir).Any(),
        };
    }

    private static bool HasCompleteGigaAmBundle(string dir)
    {
        static bool AllExist(string baseDir, string prefix) =>
            File.Exists(Path.Combine(baseDir, $"{prefix}_encoder.onnx"))
            && File.Exists(Path.Combine(baseDir, $"{prefix}_decoder.onnx"))
            && File.Exists(Path.Combine(baseDir, $"{prefix}_joint.onnx"))
            && File.Exists(Path.Combine(baseDir, $"{prefix}_tokens.txt"));

        return AllExist(dir, "gigaam_v3_e2e_rnnt")
            || AllExist(dir, "gigaam_v3_rnnt");
    }
}
