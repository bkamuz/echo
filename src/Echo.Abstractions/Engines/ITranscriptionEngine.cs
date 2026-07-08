namespace echo.Abstractions.Engines;

public sealed class EngineOptions
{
    public string Engine { get; init; } = "gigaam";
    public string WhisperModelSize { get; init; } = "small";
    public string Language { get; init; } = "ru";
    public string Device { get; init; } = "cpu";
    public int SampleRate { get; init; } = 16000;
}

public interface ITranscriptionEngine
{
    string EngineId { get; }
    string DisplayName { get; }
    void Configure(EngineOptions options);
    Task EnsureLoadedAsync(CancellationToken cancellationToken = default);
    Task<string> TranscribeAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default);
    void Unload();
}
