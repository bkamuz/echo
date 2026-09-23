using System.Reflection;
using System.Text.Json;

namespace echo.Engines.ParakeetNpu;

internal static class ManifestLoader
{
    public static QnnRuntimeManifest LoadRuntimeManifest()
    {
        using var stream = OpenEmbedded("Assets.qnn-runtime-manifest.json");
        return JsonSerializer.Deserialize<QnnRuntimeManifest>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse QNN runtime manifest.");
    }

    public static ParakeetModelManifest LoadModelManifest()
    {
        using var stream = OpenEmbedded("Assets.parakeet-npu-model-manifest.json");
        return JsonSerializer.Deserialize<ParakeetModelManifest>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse Parakeet NPU model manifest.");
    }

    private static Stream OpenEmbedded(string resourceSuffix)
    {
        var assembly = typeof(ManifestLoader).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded resource '{resourceSuffix}' not found.");
        return assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Could not open embedded resource '{name}'.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };
}

internal sealed class QnnRuntimeManifest
{
    public int SchemaVersion { get; set; }
    public string Architecture { get; set; } = string.Empty;
    public List<QnnRuntimePackage> Packages { get; set; } = [];
    public List<string> RequiredFiles { get; set; } = [];
}

internal sealed class QnnRuntimePackage
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Wheel { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public List<QnnRuntimeFileMapping> Files { get; set; } = [];
}

internal sealed class QnnRuntimeFileMapping
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
}

internal sealed class ParakeetModelManifest
{
    public int SchemaVersion { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public string TargetHardware { get; set; } = string.Empty;
    public string Languages { get; set; } = string.Empty;
    public double MaxWindowSeconds { get; set; }
    public List<ParakeetModelFile> Files { get; set; } = [];
}

internal sealed class ParakeetModelFile
{
    public string Path { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}
