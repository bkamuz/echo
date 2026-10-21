namespace echo.Abstractions.Engines;

/// <summary>Whisper Turbo NPU assets: its own ORT/QNN runtime (qnn-whisper\) and the AI Hub model.</summary>
public interface IWhisperNpuModelSupport
{
    bool IsRuntimeInstalled { get; }
    bool IsModelInstalled { get; }
    Task EnsureRuntimeAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task DownloadModelAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    void DeleteModel();
}
