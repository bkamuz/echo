namespace echo.Abstractions.Core;

public static class ModelRegistry
{
    public const string GigaAmRepo = "Smirnov75/GigaAM-v3-sherpa-onnx";
    public const string GigaAmLegacyRepo = "istupakov/gigaam-v3-onnx";
    public const string GigaAmE2ePrefix = "gigaam_v3_e2e_rnnt";
    public const string GigaAmRnntPrefix = "gigaam_v3_rnnt";

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
        $"{GigaAmE2ePrefix}_encoder.onnx",
        $"{GigaAmE2ePrefix}_decoder.onnx",
        $"{GigaAmE2ePrefix}_joint.onnx",
        $"{GigaAmE2ePrefix}_tokens.txt",
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

    public static bool HasCompleteGigaAmBundle(string dir) =>
        IsCompleteGigaAmBundle(dir, GigaAmE2ePrefix)
        || IsCompleteGigaAmBundle(dir, GigaAmRnntPrefix);

    public static GigaAmBundlePaths? ResolveGigaAmBundle(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return null;
        }

        // ponytail: e2e if downloaded, else legacy rnnt for existing installs
        var e2e = GigaAmBundlePaths.ForPrefix(dir, GigaAmE2ePrefix);
        if (IsCompleteBundle(e2e))
        {
            return e2e;
        }

        var rnnt = GigaAmBundlePaths.ForPrefix(dir, GigaAmRnntPrefix);
        return IsCompleteBundle(rnnt) ? rnnt : null;
    }

    private static bool IsCompleteGigaAmBundle(string dir, string prefix) =>
        IsCompleteBundle(GigaAmBundlePaths.ForPrefix(dir, prefix));

    private static bool IsCompleteBundle(GigaAmBundlePaths paths) =>
        File.Exists(paths.Encoder)
        && File.Exists(paths.Decoder)
        && File.Exists(paths.Joiner)
        && File.Exists(paths.Tokens);
}

public sealed record GigaAmBundlePaths(string Encoder, string Decoder, string Joiner, string Tokens)
{
    public static GigaAmBundlePaths ForPrefix(string dir, string prefix) => new(
        Path.Combine(dir, $"{prefix}_encoder.onnx"),
        Path.Combine(dir, $"{prefix}_decoder.onnx"),
        Path.Combine(dir, $"{prefix}_joint.onnx"),
        Path.Combine(dir, $"{prefix}_tokens.txt"));
}
