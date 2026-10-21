using echo.Abstractions.Core;

namespace echo.Engines.ParakeetNpu;

/// <summary>
/// Which ORT + QNN runtime a process uses. Parakeet stays on ORT 1.24 / onnxruntime-qnn 2.1.1 in qnn\;
/// Whisper Turbo NPU needs QAIRT 2.50 (onnxruntime-qnn 2.6.0 + ORT 1.30) in qnn-whisper\.
/// One process loads exactly one onnxruntime.dll, so each engine runs in its own worker process.
/// </summary>
public sealed class QnnRuntimeProfile
{
    private QnnRuntimeProfile(string name, Func<string> dir, string manifestResource)
    {
        Name = name;
        _dir = dir;
        ManifestResource = manifestResource;
    }

    private readonly Func<string> _dir;

    public string Name { get; }
    public string Dir => _dir();
    public string ManifestResource { get; }

    public static QnnRuntimeProfile Parakeet { get; } =
        new("parakeet", () => AppPaths.NpuDir, "Assets.qnn-runtime-manifest.json");

    public static QnnRuntimeProfile Whisper { get; } =
        new("whisper", () => AppPaths.NpuWhisperRuntimeDir, "Assets.qnn-runtime-whisper-manifest.json");

    /// <summary>Profile of the runtime loaded by OrtQnnBootstrap in this process.</summary>
    public static QnnRuntimeProfile Active { get; internal set; } = Parakeet;

    public static QnnRuntimeProfile FromName(string? name) =>
        string.Equals(name, Whisper.Name, StringComparison.OrdinalIgnoreCase) ? Whisper : Parakeet;
}
