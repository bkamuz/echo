using echo.Abstractions.Engines;
using echo.Engines.ParakeetNpu.Parakeet;
using Microsoft.Extensions.Logging;

namespace echo.Engines.ParakeetNpu;

public sealed class ParakeetNpuEngine : ITranscriptionEngine, IDisposable
{
    private readonly QnnRuntimeDownloader _runtimeDownloader;
    private readonly ParakeetModelDownloader _modelDownloader;
    private readonly ILogger<ParakeetNpuEngine> _logger;

    private ParakeetPipeline? _pipeline;
    private string _resolvedProvider = "cpu";
    private string _configuredDevice = ExecutionProviderResolver.CpuDevice;
    private bool? _loadedUseNpu;

    public ParakeetNpuEngine(
        QnnRuntimeDownloader runtimeDownloader,
        ParakeetModelDownloader modelDownloader,
        ILogger<ParakeetNpuEngine> logger)
    {
        _runtimeDownloader = runtimeDownloader;
        _modelDownloader = modelDownloader;
        _logger = logger;
    }

    public string EngineId => "parakeet_npu";

    public string DisplayName => "Parakeet NPU (experimental)";

    public string ResolvedProvider => _resolvedProvider;

    public void Configure(EngineOptions options)
    {
        _configuredDevice = ExecutionProviderResolver.ToConfigDevice(
            ExecutionProviderResolver.FromConfigDevice(options.Device));

        var wantNpu = ExecutionProviderResolver.FromConfigDevice(_configuredDevice) == ExecutionProvider.Npu;
        if (_pipeline is not null && _loadedUseNpu != wantNpu)
        {
            Unload();
        }
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        var useNpu = ExecutionProviderResolver.FromConfigDevice(_configuredDevice) == ExecutionProvider.Npu;
        if (_pipeline is not null && _loadedUseNpu == useNpu)
        {
            return;
        }

        Unload();

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Parakeet NPU is Windows-only.");
        }

        SnapdragonHardware.EnsureWindowsArm64();

        var modelDir = ParakeetModelDownloader.ModelDir;

        if (useNpu)
        {
            SnapdragonHardware.LogHtpCompatibilityWarning(_logger);

            var missingRuntime = _runtimeDownloader.GetMissingFiles();
            if (missingRuntime.Count > 0)
            {
                _logger.LogInformation(
                    "Parakeet NPU requires QNN runtime ({MissingCount} file(s) missing); installing to {Dir}",
                    missingRuntime.Count,
                    QnnRuntimeDownloader.RuntimeDir);
            }

            await _runtimeDownloader.EnsureInstalledAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await _modelDownloader.EnsureInstalledAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var runtimeDir = QnnRuntimeDownloader.RuntimeDir;
            var htpProfile = HtpHardwareProfile.Resolve();
            LogX2ContextBinaryGuidanceIfNeeded(htpProfile);

            try
            {
                _logger.LogInformation(
                    "Loading Parakeet NPU pipeline (provider=qnn/htp, model={ModelDir}, htp_arch={HtpArch})",
                    modelDir,
                    htpProfile.HtpArch);
                _pipeline = ParakeetPipeline.LoadNpu(modelDir, runtimeDir, _logger);
                _resolvedProvider = "qnn/htp";
                _loadedUseNpu = true;
                _logger.LogInformation(
                    "Parakeet NPU ready — encoder on Hexagon HTP via ORT QNN EP (htp_arch={HtpArch})",
                    htpProfile.HtpArch);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Parakeet NPU (HTP) load failed");
                throw new InvalidOperationException(BuildNpuLoadFailureMessage(htpProfile, ex), ex);
            }

            return;
        }

        await _modelDownloader.EnsureInstalledAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await _modelDownloader.EnsureCpuEncoderAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        try
        {
            _logger.LogInformation(
                "Loading Parakeet CPU pipeline (provider=cpu, model={ModelDir})",
                modelDir);
            _pipeline = ParakeetPipeline.LoadCpu(modelDir, _logger);
            _resolvedProvider = "cpu";
            _loadedUseNpu = false;
            _logger.LogInformation("Parakeet CPU ready — encoder on ORT CPU EP");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Parakeet CPU load failed");
            throw new InvalidOperationException(
                "Could not load Parakeet on CPU. Check echo.log for ONNX runtime details.", ex);
        }
    }

    public Task<string> TranscribeAsync(
        float[] samples,
        int sampleRate,
        CancellationToken cancellationToken = default)
    {
        if (_pipeline is null)
        {
            throw new InvalidOperationException("Parakeet NPU model is not loaded.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var text = _pipeline.Transcribe(samples, sampleRate, _logger);
        _logger.LogInformation(
            "Parakeet transcribe complete provider={Provider} chars={Chars}",
            _resolvedProvider,
            text.Length);
        return Task.FromResult(text);
    }

    public void Unload()
    {
        _pipeline?.Dispose();
        _pipeline = null;
        _loadedUseNpu = null;
        _resolvedProvider = "cpu";
    }

    public void Dispose() => Unload();

    private void LogX2ContextBinaryGuidanceIfNeeded(HtpHardwareProfile profile)
    {
        if (profile.Generation != HexagonHtpGeneration.V81)
        {
            return;
        }

        var manifest = ManifestLoader.LoadModelManifest();
        if (manifest.TargetHardware.Contains("V73", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Parakeet encoder context binary targets {ModelTarget}, but this device needs {DeviceTarget}. "
                + "QNN runtime will use htp_arch={HtpArch}; if session creation fails, a V81 context binary "
                + "must be compiled for X2 Elite (no published Echo asset yet). CPU fallback remains available.",
                manifest.TargetHardware,
                profile.ContextTarget,
                profile.HtpArch);
        }
    }

    private static string BuildNpuLoadFailureMessage(HtpHardwareProfile profile, Exception ex)
    {
        var baseMessage =
            "Could not load Parakeet on the Hexagon NPU. Check echo.log for QNN/HTP details "
            + "(missing skel/cat, wrong SoC generation, or incomplete runtime).";

        if (profile.Generation != HexagonHtpGeneration.V81)
        {
            return baseMessage;
        }

        var detail = ex.Message;
        if (detail.Contains("context", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("htp", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("QNN", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("binary", StringComparison.OrdinalIgnoreCase))
        {
            return baseMessage
                + $" This Snapdragon X2 Elite device requires Hexagon V81 HTP assets and a V81 encoder context binary; "
                + $"the shipped Parakeet model is compiled for V73 (X Elite). Use CPU mode until a V81 context is published.";
        }

        return baseMessage
            + $" Ensure {profile.StubDll}, {profile.SkelSo}, and {profile.CatalogCat} are present under %APPDATA%\\Echo\\qnn\\.";
    }
}
