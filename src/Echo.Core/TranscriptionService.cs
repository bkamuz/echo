using echo.Abstractions.Engines;

namespace echo.Core;

public sealed class TranscriptionService
{
    private readonly IEnumerable<ITranscriptionEngine> _engines;

    public TranscriptionService(IEnumerable<ITranscriptionEngine> engines)
    {
        _engines = engines;
    }

    public ITranscriptionEngine Resolve(AppConfig config)
    {
        var engineId = config.Engine;
        var engine = _engines.FirstOrDefault(e => e.EngineId == engineId)
            ?? throw new InvalidOperationException($"Engine '{engineId}' is not registered.");

        engine.Configure(new EngineOptions
        {
            Engine = config.Engine,
            WhisperModelSize = config.WhisperModelSize,
            Language = config.Language,
            Device = config.Device,
            SampleRate = config.SampleRate,
        });
        return engine;
    }

    public async Task<string> TranscribeAsync(AppConfig config, float[] samples, CancellationToken cancellationToken = default)
    {
        var engine = Resolve(config);
        await engine.EnsureLoadedAsync(cancellationToken);
        return await engine.TranscribeAsync(samples, config.SampleRate, cancellationToken);
    }

    public async Task WarmupAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        var engine = Resolve(config);
        await engine.EnsureLoadedAsync(cancellationToken);
    }
}
