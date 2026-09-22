using echo.Abstractions.Engines;
using Microsoft.Extensions.Logging;

namespace echo.Platform.Windows;

/// <summary>
/// Routes Sherpa/GigaAM work through an isolated child process on Windows ARM64.
/// </summary>
public sealed class SherpaOutOfProcessEngine : ITranscriptionEngine, IDisposable
{
    private readonly SherpaWorkerClient _worker;
    private readonly string _engineId;
    private EngineOptions _options = new();
    private string _displayName = string.Empty;

    public SherpaOutOfProcessEngine(string engineId, ILogger logger)
    {
        _engineId = engineId;
        _worker = new SherpaWorkerClient(logger);
    }

    public string EngineId => _engineId;

    public string DisplayName =>
        string.IsNullOrEmpty(_displayName)
            ? FormatDisplayName(_engineId, _options)
            : _displayName;

    public void Configure(EngineOptions options) => _options = CloneOptions(options);

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        await _worker.EnsureReadyAsync(_engineId, _options, cancellationToken).ConfigureAwait(false);
        _displayName = _worker.LoadedDisplayName;
    }

    public Task<string> TranscribeAsync(
        float[] samples,
        int sampleRate,
        CancellationToken cancellationToken = default) =>
        _worker.TranscribeAsync(samples, sampleRate, cancellationToken);

    public void Unload()
    {
        _worker.Unload();
        _displayName = string.Empty;
    }

    public void Dispose() => _worker.Dispose();

    private static EngineOptions CloneOptions(EngineOptions options) => new()
    {
        Engine = options.Engine,
        WhisperModelSize = options.WhisperModelSize,
        GigaAmModelSize = options.GigaAmModelSize,
        Language = options.Language,
        Device = options.Device,
        SampleRate = options.SampleRate,
    };

    private static string FormatDisplayName(string engineId, EngineOptions options)
    {
        var device = ExecutionProviderResolver
            .FromConfigDevice(options.Device)
            .ToString()
            .ToUpperInvariant();

        return engineId switch
        {
            "gigaam" => options.GigaAmModelSize switch
            {
                "rnnt" => $"GigaAM v3 rnnt ({device})",
                "e2e-ctc" => $"GigaAM v3 e2e-ctc ({device})",
                "multilingual" => $"GigaAM Multilingual CTC ({device})",
                "multilingual-large" => $"GigaAM Multilingual Large CTC ({device})",
                _ => $"GigaAM v3 e2e ({device})",
            },
            "whisper" => $"Whisper {options.WhisperModelSize} ({device})",
            "omnilingual" => $"Omnilingual ASR 300M ({device})",
            _ => engineId,
        };
    }
}
