using echo.Abstractions.Engines;

namespace echo.Core;

public sealed class TranscriptionService
{
    private readonly IEnumerable<ITranscriptionEngine> _engines;
    private readonly SemaphoreSlim _engineGate = new(1, 1);

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
            GigaAmModelSize = config.GigaAmModelSize,
            Language = config.Language,
            Device = config.Device,
            SampleRate = config.SampleRate,
        });
        return engine;
    }

    public Task<string> TranscribeAsync(AppConfig config, float[] samples, CancellationToken cancellationToken = default) =>
        WithEngineGateAsync(
            config,
            async (engine, ct) =>
            {
                await engine.EnsureLoadedAsync(ct).ConfigureAwait(false);
                return await engine.TranscribeAsync(samples, config.SampleRate, ct).ConfigureAwait(false);
            },
            cancellationToken);

    public Task WarmupAsync(AppConfig config, CancellationToken cancellationToken = default) =>
        WithEngineGateAsync(
            config,
            (engine, ct) => engine.EnsureLoadedAsync(ct),
            cancellationToken);

    private async Task WithEngineGateAsync(
        AppConfig config,
        Func<ITranscriptionEngine, CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        await _engineGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var engine = Resolve(config);
            await Task.Run(
                () => work(engine, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _engineGate.Release();
        }
    }

    private async Task<T> WithEngineGateAsync<T>(
        AppConfig config,
        Func<ITranscriptionEngine, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        await _engineGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var engine = Resolve(config);
            return await Task.Run(
                () => work(engine, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _engineGate.Release();
        }
    }
}
