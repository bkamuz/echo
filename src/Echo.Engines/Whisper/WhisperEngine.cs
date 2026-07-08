using echo.Abstractions.Core;
using echo.Abstractions.Engines;
using Microsoft.Extensions.Logging;
using Whisper.net;
using Whisper.net.Ggml;

namespace echo.Engines.Whisper;

public sealed class WhisperEngine : ITranscriptionEngine, IDisposable
{
    private readonly ILogger<WhisperEngine> _logger;
    private EngineOptions _config = new();
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private string _resolvedDevice = "cpu";
    private string _loadedLanguage = string.Empty;

    public WhisperEngine(ILogger<WhisperEngine> logger)
    {
        _logger = logger;
    }

    public string EngineId => "whisper";
    public string DisplayName => $"Whisper {_config.WhisperModelSize} ({_resolvedDevice.ToUpperInvariant()})";

    public void Configure(EngineOptions options) => _config = options;

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        _resolvedDevice = _config.Device;
        var modelPath = await EnsureGgmlModelAsync(cancellationToken);

        if (_factory is not null && _loadedLanguage == _config.Language && _processor is not null)
        {
            return;
        }

        _processor?.Dispose();
        _factory?.Dispose();

        _logger.LogInformation("Loading Whisper {Size} from {Path}", _config.WhisperModelSize, modelPath);
        _factory = WhisperFactory.FromPath(modelPath);
        _processor = _factory.CreateBuilder()
            .WithLanguage(_config.Language)
            .Build();
        _loadedLanguage = _config.Language;
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
    }

    public void Dispose() => Unload();

    private async Task<string> EnsureGgmlModelAsync(CancellationToken cancellationToken)
    {
        var whisperDir = AppPaths.WhisperDir(_config.WhisperModelSize);
        Directory.CreateDirectory(whisperDir);

        var ggmlPath = Path.Combine(whisperDir, $"ggml-{_config.WhisperModelSize}.bin");
        if (File.Exists(ggmlPath))
        {
            return ggmlPath;
        }

        var ggmlType = GgmlType.Base;
        if (_config.WhisperModelSize.Contains("large", StringComparison.OrdinalIgnoreCase))
        {
            ggmlType = GgmlType.LargeV3;
        }
        else if (Enum.TryParse<GgmlType>(_config.WhisperModelSize, true, out var parsed))
        {
            ggmlType = parsed;
        }

        _logger.LogInformation("Downloading ggml Whisper model {Type}", ggmlType);
        using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(ggmlType, cancellationToken: cancellationToken);
        await using var file = File.Create(ggmlPath);
        await modelStream.CopyToAsync(file, cancellationToken);
        return ggmlPath;
    }

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
