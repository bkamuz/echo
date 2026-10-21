using echo.Abstractions.Engines;
using Microsoft.Extensions.Logging;

namespace echo.Engines.ParakeetNpu.Whisper;

/// <summary>Whisper Large-V3-Turbo on the Hexagon NPU (QAIRT 2.50 runtime in qnn-whisper\). Runs inside the worker process.</summary>
public sealed class WhisperNpuEngine : ITranscriptionEngine, IDisposable
{
    public const string Id = "whisper_npu";

    private readonly QnnRuntimeDownloader _runtimeDownloader;
    private readonly WhisperNpuModelDownloader _modelDownloader;
    private readonly ILogger<WhisperNpuEngine> _logger;

    private WhisperQnnRunner? _runner;
    private WhisperTranscriber? _transcriber;
    private string _language = "ru";

    public WhisperNpuEngine(
        QnnRuntimeDownloader runtimeDownloader,
        WhisperNpuModelDownloader modelDownloader,
        ILogger<WhisperNpuEngine> logger)
    {
        if (runtimeDownloader.Profile != QnnRuntimeProfile.Whisper)
        {
            throw new ArgumentException("Whisper NPU requires the Whisper QNN runtime profile.", nameof(runtimeDownloader));
        }

        _runtimeDownloader = runtimeDownloader;
        _modelDownloader = modelDownloader;
        _logger = logger;
    }

    public string EngineId => Id;

    public string DisplayName => "Whisper Turbo (NPU)";

    public string ResolvedProvider => _runner is null ? "none" : "qnn/htp";

    public void Configure(EngineOptions options)
    {
        _language = options.Language;
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_runner is not null)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Whisper NPU is Windows-only.");
        }

        SnapdragonHardware.EnsureWindowsArm64();
        SnapdragonHardware.LogHtpCompatibilityWarning(_logger);
        var htp = HtpHardwareProfile.Resolve();
        _logger.LogInformation(
            "Whisper NPU: target {Target} (htp_arch={Arch}, soc_model={Soc}, asset={Chipset})",
            htp.ContextTarget,
            htp.HtpArch,
            htp.SocModel,
            WhisperNpuModelDownloader.ChipsetKey(htp));

        var missing = _runtimeDownloader.GetMissingFiles();
        if (missing.Count > 0)
        {
            _logger.LogInformation(
                "Whisper NPU: installing QNN runtime (ORT 1.30 + onnxruntime-qnn 2.6.0) to {Dir}; missing {Files}",
                _runtimeDownloader.RuntimeDir,
                string.Join(", ", missing));
        }

        await _runtimeDownloader.EnsureInstalledAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await _modelDownloader.EnsureInstalledAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var modelDir = WhisperNpuModelDownloader.ModelDir;
        try
        {
            var tokenizer = WhisperTokenizer.Load(Path.Combine(modelDir, "vocab.json"));
            _runner = WhisperQnnRunner.Load(modelDir, _runtimeDownloader.RuntimeDir, _logger);
            _transcriber = new WhisperTranscriber(_runner, tokenizer, _logger);
            _logger.LogInformation("Whisper NPU ready (model={ModelDir}, vocab={Vocab})", modelDir, tokenizer.VocabSize);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Whisper NPU (HTP) load failed");
            Unload();
            throw new InvalidOperationException(
                $"Could not load Whisper Turbo on the NPU ({htp.ContextTarget}): {ex.Message}", ex);
        }
    }

    public Task<string> TranscribeAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
    {
        if (_transcriber is null)
        {
            throw new InvalidOperationException("Whisper NPU is not loaded.");
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var audio = WhisperTranscriber.ResampleTo16k(samples, sampleRate);
        var text = _transcriber.Transcribe(audio, _language, cancellationToken);
        _logger.LogInformation(
            "Whisper NPU transcribed {Seconds:F1}s in {Ms} ms ({Chars} chars, language={Language})",
            audio.Length / 16000.0,
            sw.ElapsedMilliseconds,
            text.Length,
            _language);
        return Task.FromResult(text);
    }

    public void Unload()
    {
        _transcriber = null;
        _runner?.Dispose();
        _runner = null;
    }

    public void Dispose() => Unload();
}
