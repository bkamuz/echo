namespace echo.Abstractions.Core;

public sealed record ModelSpec(
    string Id,
    string Title,
    string Engine,
    string RepoId,
    string LocalDir,
    IReadOnlyList<string>? AllowPatterns = null,
    string? GitHubReleaseTag = null)
{
    public bool IsDownloaded()
    {
        if (!Directory.Exists(LocalDir))
        {
            return false;
        }

        return Engine switch
        {
            "gigaam" => ModelRegistry.IsGigaAmVariantDownloaded(
                LocalDir,
                ModelRegistry.GigaAmVariantFromSpecId(Id)),
            "whisper" => ModelRegistry.IsWhisperDownloaded(ModelRegistry.WhisperSizeFromSpecId(Id)),
            "parakeet_npu" => IsParakeetNpuDownloaded(),
            _ => Directory.EnumerateFiles(LocalDir).Any(),
        };
    }

    private bool IsParakeetNpuDownloaded()
    {
        if (!Directory.Exists(LocalDir))
        {
            return false;
        }

        var required = new[]
        {
            "encoder-model.onnx",
            "encoder-model.bin",
            "decoder_joint-model.int8.onnx",
            "nemo128.onnx",
            "vocab.txt",
        };
        return required.All(file => File.Exists(Path.Combine(LocalDir, file)));
    }
}
