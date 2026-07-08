namespace echo.Abstractions.Core;

public static class ModelRegistry
{
    public const string GigaAmRepo = "Smirnov75/GigaAM-v3-sherpa-onnx";
    public const string GigaAmLegacyRepo = "istupakov/gigaam-v3-onnx";

    public static IReadOnlyList<string> WhisperSizes { get; } =
        ["tiny", "base", "small", "medium", "large-v3", "large-v3-turbo"];

    private static readonly Dictionary<string, string> WhisperRepos = new()
    {
        ["tiny"] = "Systran/faster-whisper-tiny",
        ["base"] = "Systran/faster-whisper-base",
        ["small"] = "Systran/faster-whisper-small",
        ["medium"] = "Systran/faster-whisper-medium",
        ["large-v3"] = "Systran/faster-whisper-large-v3",
        ["large-v3-turbo"] = "mobiuslabsgmbh/faster-whisper-large-v3-turbo",
    };

    public static IReadOnlyList<string> GigaAmAllowPatterns { get; } =
    [
        "gigaam_v3_rnnt_encoder.onnx",
        "gigaam_v3_rnnt_decoder.onnx",
        "gigaam_v3_rnnt_joint.onnx",
        "gigaam_v3_rnnt_tokens.txt",
        "config.json",
    ];

    public static ModelSpec GigaAmSpec() => new(
        Id: "gigaam-v3",
        Title: "GigaAM v3",
        Engine: "gigaam",
        RepoId: GigaAmRepo,
        LocalDir: AppPaths.GigaAmDir,
        AllowPatterns: GigaAmAllowPatterns);

    public static ModelSpec WhisperSpec(string size) => new(
        Id: $"whisper-{size}",
        Title: $"Whisper {size}",
        Engine: "whisper",
        RepoId: WhisperRepos[size],
        LocalDir: AppPaths.WhisperDir(size));

    public static IReadOnlyList<ModelSpec> AllModels()
    {
        var models = new List<ModelSpec> { GigaAmSpec() };
        models.AddRange(WhisperSizes.Select(WhisperSpec));
        return models;
    }

    public static ModelSpec? GetModel(string id) =>
        AllModels().FirstOrDefault(m => m.Id == id);
}
