namespace echo.Abstractions.Engines;

/// <summary>
/// Resolves transcription engines on first use so native-bearing assemblies are not
/// loaded during application startup.
/// </summary>
public interface ITranscriptionEngineRegistry
{
    IReadOnlyCollection<string> RegisteredEngineIds { get; }

    bool IsRegistered(string engineId);

    ITranscriptionEngine GetRequired(string engineId);
}

public sealed class EngineRegistration
{
    public required string EngineId { get; init; }

    public required Func<IServiceProvider, ITranscriptionEngine> Factory { get; init; }
}
