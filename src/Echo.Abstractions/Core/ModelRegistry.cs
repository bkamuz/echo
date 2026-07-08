namespace echo.Abstractions.Core;

public static class ModelRegistry
{
    public const string GigaAmRepo = "Smirnov75/GigaAM-v3-sherpa-onnx";
    public const string GigaAmE2ePrefix = "gigaam_v3_e2e_rnnt";
    public const string GigaAmRnntPrefix = "gigaam_v3_rnnt";

    public static IReadOnlyList<string> WhisperSizes { get; } =
        ["tiny", "base", "small", "medium", "large-v3", "large-v3-turbo"];

    public static IReadOnlyList<string> GigaAmE2eAllowPatterns { get; } =
    [
        $"{GigaAmE2ePrefix}_encoder_int8.onnx",
        $"{GigaAmE2ePrefix}_encoder.onnx",
        $"{GigaAmE2ePrefix}_decoder.onnx",
        $"{GigaAmE2ePrefix}_joint.onnx",
        $"{GigaAmE2ePrefix}_tokens.txt",
        "config.json",
    ];

    public static IReadOnlyList<string> GigaAmRnntAllowPatterns { get; } =
    [
        $"{GigaAmRnntPrefix}_encoder_int8.onnx",
        $"{GigaAmRnntPrefix}_encoder.onnx",
        $"{GigaAmRnntPrefix}_decoder.onnx",
        $"{GigaAmRnntPrefix}_joint.onnx",
        $"{GigaAmRnntPrefix}_tokens.txt",
        "config.json",
    ];

    public static string WhisperGgmlPath(string size) =>
        Path.Combine(AppPaths.WhisperDir(size), $"ggml-{size}.bin");

    public static bool IsWhisperDownloaded(string size) =>
        File.Exists(WhisperGgmlPath(size));

    public static string WhisperSizeFromSpecId(string specId) =>
        specId.StartsWith("whisper-", StringComparison.Ordinal) ? specId["whisper-".Length..] : specId;

    public static ModelSpec WhisperSpec(string size) => new(
        Id: $"whisper-{size}",
        Title: $"Whisper {size}",
        Engine: "whisper",
        RepoId: string.Empty,
        LocalDir: AppPaths.WhisperDir(size));

    public const string OmnilingualRepo = "csukuangfj2/sherpa-onnx-omnilingual-asr-1600-languages-300M-ctc-int8-2025-11-12";

    public static IReadOnlyList<string> OmnilingualAllowPatterns { get; } =
    [
        "model.int8.onnx",
        "tokens.txt",
    ];

    public static ModelSpec OmnilingualSpec() => new(
        Id: "omnilingual-300m",
        Title: "Omnilingual ASR 300M",
        Engine: "omnilingual",
        RepoId: OmnilingualRepo,
        LocalDir: AppPaths.OmnilingualDir,
        AllowPatterns: OmnilingualAllowPatterns);

    public static IReadOnlyList<string> GigaAmSizes { get; } = ["e2e", "rnnt"];

    public static ModelSpec? GigaAmSpecFor(string variant) => variant switch
    {
        "e2e" => new ModelSpec(
            Id: "gigaam-v3-e2e",
            Title: "GigaAM v3 e2e",
            Engine: "gigaam",
            RepoId: GigaAmRepo,
            LocalDir: AppPaths.GigaAmDir,
            AllowPatterns: GigaAmE2eAllowPatterns),
        "rnnt" => new ModelSpec(
            Id: "gigaam-v3-rnnt",
            Title: "GigaAM v3 rnnt",
            Engine: "gigaam",
            RepoId: GigaAmRepo,
            LocalDir: AppPaths.GigaAmDir,
            AllowPatterns: GigaAmRnntAllowPatterns),
        _ => null,
    };

    public static string GigaAmVariantFromSpecId(string specId) => specId switch
    {
        "gigaam-v3-rnnt" => "rnnt",
        _ => "e2e",
    };

    public static GigaAmBundlePaths? ResolveGigaAmBundle(string dir, string variant)
    {
        if (!Directory.Exists(dir))
        {
            return null;
        }

        var prefix = variant switch
        {
            "rnnt" => GigaAmRnntPrefix,
            "e2e" => GigaAmE2ePrefix,
            _ => null,
        };
        if (prefix is null)
        {
            return null;
        }

        var int8 = GigaAmBundlePaths.ForInt8(dir, prefix);
        if (IsCompleteBundle(int8))
        {
            return int8;
        }

        var fp32 = GigaAmBundlePaths.ForPrefix(dir, prefix);
        return IsCompleteBundle(fp32) ? fp32 : null;
    }

    public static IReadOnlyList<ModelSpec> AllModels()
    {
        var models = new List<ModelSpec>
        {
            GigaAmSpecFor("e2e")!,
            GigaAmSpecFor("rnnt")!,
            OmnilingualSpec(),
        };
        models.AddRange(WhisperSizes.Select(WhisperSpec));
        return models;
    }

    public static ModelSpec? GetModel(string id) =>
        AllModels().FirstOrDefault(m => m.Id == id);

    public static ModelSpec? SpecForEngine(string engine, string whisperModelSize, string gigaAmModelSize) =>
        engine switch
        {
            "gigaam" => GigaAmSpecFor(gigaAmModelSize),
            "whisper" => WhisperSpec(whisperModelSize),
            "omnilingual" => OmnilingualSpec(),
            _ => null,
        };

    public static bool IsEngineModelDownloaded(string engine, string whisperModelSize, string gigaAmModelSize) =>
        SpecForEngine(engine, whisperModelSize, gigaAmModelSize)?.IsDownloaded() ?? false;

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

    public static GigaAmBundlePaths ForInt8(string dir, string prefix) => new(
        Path.Combine(dir, $"{prefix}_encoder_int8.onnx"),
        Path.Combine(dir, $"{prefix}_decoder.onnx"),
        Path.Combine(dir, $"{prefix}_joint.onnx"),
        Path.Combine(dir, $"{prefix}_tokens.txt"));
}
