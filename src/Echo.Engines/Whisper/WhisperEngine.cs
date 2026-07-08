using echo.Abstractions.Engines;
using Microsoft.Extensions.Logging;
using Whisper.net;

namespace echo.Engines.Whisper;

public sealed class WhisperEngine : ITranscriptionEngine, IDisposable
{
    private readonly ILogger<WhisperEngine> _logger;
    private EngineOptions _config = new();
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private string _resolvedDevice = "cpu";
    private string _loadedLanguage = string.Empty;
    private string _loadedModelPath = string.Empty;
    private int _boundThreadId = -1;

    public WhisperEngine(ILogger<WhisperEngine> logger)
    {
        _logger = logger;
    }

    public string EngineId => "whisper";
    public string DisplayName => $"Whisper {_config.WhisperModelSize} ({_resolvedDevice.ToUpperInvariant()})";

    public void Configure(EngineOptions options) => _config = options;

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        ReleaseIfWrongThread();

        _resolvedDevice = _config.Device;
        var modelPath = WhisperGgmlHelper.ResolveGgmlModelPath(_config.WhisperModelSize);

        if (_factory is not null
            && _loadedLanguage == _config.Language
            && _loadedModelPath == modelPath
            && _processor is not null)
        {
            return;
        }

        _processor?.Dispose();
        _factory?.Dispose();

        _logger.LogInformation("Loading Whisper {Size} from {Path}", _config.WhisperModelSize, modelPath);
        _factory = WhisperFactory.FromPath(modelPath);
        var builder = _factory.CreateBuilder();
        if (!string.IsNullOrEmpty(_config.Language) && _config.Language != "auto")
        {
            builder = builder.WithLanguage(_config.Language);
        }
        _processor = builder.Build();
        _loadedLanguage = _config.Language;
        _loadedModelPath = modelPath;
        _boundThreadId = Environment.CurrentManagedThreadId;
    }

    public async Task<string> TranscribeAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        if (_processor is null)
        {
            return string.Empty;
        }

        await using var memory = new MemoryStream();
        WriteWav(memory, samples, sampleRate);
        memory.Position = 0;

        var segments = new List<string>();
        await foreach (var segment in _processor.ProcessAsync(memory, cancellationToken))
        {
            segments.Add(segment.Text);
        }

        return string.Join(' ', segments).Trim();
    }

    public void Unload()
    {
        _processor?.Dispose();
        _processor = null;
        _factory?.Dispose();
        _factory = null;
        _loadedLanguage = string.Empty;
        _loadedModelPath = string.Empty;
        _boundThreadId = -1;
    }

    private void ReleaseIfWrongThread()
    {
        if (_processor is null || _boundThreadId == Environment.CurrentManagedThreadId)
        {
            return;
        }

        _logger.LogDebug("Whisper processor loaded on another thread; reloading on current thread");
        Unload();
    }

    public void Dispose() => Unload();

    private static void WriteWav(Stream stream, float[] samples, int sampleRate)
    {
        var pcmByteCount = samples.Length * 2;
        var pcm = new byte[pcmByteCount];
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = (short)Math.Clamp(samples[i] * short.MaxValue, short.MinValue, short.MaxValue);
            pcm[i * 2] = (byte)(sample & 0xFF);
            pcm[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }

        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        var byteRate = sampleRate * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + pcmByteCount);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(pcmByteCount);
        writer.Write(pcm);
    }
}
