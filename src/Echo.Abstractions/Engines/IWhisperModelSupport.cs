namespace echo.Abstractions.Engines;

/// <summary>
/// Optional Whisper model download/delete. Absent when the build excludes Whisper.
/// </summary>
public interface IWhisperModelSupport
{
    Task DownloadAsync(string modelSize, IProgress<string>? progress, CancellationToken cancellationToken = default);

    void Delete(string modelSize);
}
