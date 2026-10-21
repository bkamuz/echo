using echo.Engines.ParakeetNpu.Ort;
using Microsoft.Extensions.Logging;

namespace echo.Engines.ParakeetNpu.Whisper;

/// <summary>Runs the AI Hub precompiled QNN encoder/decoder (fp16 I/O) through the ORT C API.</summary>
internal sealed class WhisperQnnRunner : IWhisperModelRunner, IDisposable
{
    public const int Layers = 4;
    public const int Heads = 20;
    public const int HeadDim = 64;
    public const int AudioCtx = 1500;
    public const int CtxLength = 200;
    public const int VocabSize = 51866;

    private static readonly long[] KSelfShape = [Heads, 1, HeadDim, CtxLength - 1];
    private static readonly long[] VSelfShape = [Heads, 1, CtxLength - 1, HeadDim];
    private static readonly long[] KCrossShape = [Heads, 1, HeadDim, AudioCtx];
    private static readonly long[] VCrossShape = [Heads, 1, AudioCtx, HeadDim];

    private readonly OrtQnnSession _encoder;
    private readonly OrtQnnSession _decoder;
    private readonly ILogger? _logger;
    private readonly Half[][] _kCross = new Half[Layers][];
    private readonly Half[][] _vCross = new Half[Layers][];
    private readonly Half[][] _kSelf = new Half[Layers][];
    private readonly Half[][] _vSelf = new Half[Layers][];

    private WhisperQnnRunner(OrtQnnSession encoder, OrtQnnSession decoder, ILogger? logger)
    {
        _encoder = encoder;
        _decoder = decoder;
        _logger = logger;
    }

    public int DecodeLength => CtxLength;

    public static WhisperQnnRunner Load(string modelDir, string runtimeDir, ILogger? logger)
    {
        logger?.LogInformation("Whisper NPU: initializing ORT/QNN runtime from {RuntimeDir}", runtimeDir);
        OrtQnnBootstrap.Initialize(runtimeDir, logger);
        logger?.LogInformation("Whisper NPU: ORT version {Version}", OrtApiNative.LoadedVersionString ?? "unknown");

        var encoderLease = OrtQnnBootstrap.AcquireQnnProvider(logger);
        var devices = OrtQnnBootstrap.EnumerateQnnNpuDevices(logger);
        if (devices.Count == 0)
        {
            encoderLease.Dispose();
            throw new InvalidOperationException("No QNN NPU device discovered by ORT.");
        }

        var started = System.Diagnostics.Stopwatch.StartNew();
        logger?.LogInformation("Whisper NPU: creating encoder session ({Path})", Path.Combine(modelDir, "encoder.onnx"));
        var encoder = OrtQnnSession.Load(Path.Combine(modelDir, "encoder.onnx"), devices, EncoderContract, encoderLease, logger);
        logger?.LogInformation("Whisper NPU: encoder session ready in {Ms} ms", started.ElapsedMilliseconds);

        OrtQnnSession decoder;
        try
        {
            started.Restart();
            var decoderLease = OrtQnnBootstrap.AcquireQnnProvider(logger);
            logger?.LogInformation("Whisper NPU: creating decoder session ({Path})", Path.Combine(modelDir, "decoder.onnx"));
            decoder = OrtQnnSession.Load(Path.Combine(modelDir, "decoder.onnx"), devices, DecoderContract, decoderLease, logger);
            logger?.LogInformation("Whisper NPU: decoder session ready in {Ms} ms", started.ElapsedMilliseconds);
        }
        catch
        {
            encoder.Dispose();
            throw;
        }

        return new WhisperQnnRunner(encoder, decoder, logger);
    }

    public void Encode(Half[] features)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var outputs = _encoder.Run([new OrtTensorInput("input_features", [1, 128, 3000], features)]);
        foreach (var output in outputs)
        {
            var (kind, layer) = ParseCrossName(output.Name);
            if (kind == 'k')
            {
                _kCross[layer] = output.IntoHalf();
            }
            else
            {
                _vCross[layer] = output.IntoHalf();
            }
        }

        for (var i = 0; i < Layers; i++)
        {
            _kSelf[i] = new Half[Heads * HeadDim * (CtxLength - 1)];
            _vSelf[i] = new Half[Heads * HeadDim * (CtxLength - 1)];
        }

        _logger?.LogDebug("Whisper NPU: encoder run {Ms} ms", sw.ElapsedMilliseconds);
    }

    public float[] Decode(int tokenId, int position, Half[] attentionMask)
    {
        var inputs = new List<OrtTensorInput>(3 + Layers * 4)
        {
            new("input_ids", [1, 1], new[] { tokenId }),
            new("position_ids", [1], new[] { position }),
            new("attention_mask", [1, 1, 1, CtxLength], attentionMask),
        };
        for (var i = 0; i < Layers; i++)
        {
            inputs.Add(new($"k_cache_self_{i}_in", KSelfShape, _kSelf[i]));
            inputs.Add(new($"v_cache_self_{i}_in", VSelfShape, _vSelf[i]));
            inputs.Add(new($"k_cache_cross_{i}", KCrossShape, _kCross[i]));
            inputs.Add(new($"v_cache_cross_{i}", VCrossShape, _vCross[i]));
        }

        float[]? logits = null;
        foreach (var output in _decoder.Run(inputs))
        {
            if (output.Name == "logits")
            {
                var half = output.IntoHalf();
                logits = new float[half.Length];
                for (var j = 0; j < half.Length; j++)
                {
                    logits[j] = (float)half[j];
                }

                continue;
            }

            // k_cache_self_{i}_out / v_cache_self_{i}_out
            var layer = output.Name[13] - '0';
            if (output.Name[0] == 'k')
            {
                _kSelf[layer] = output.IntoHalf();
            }
            else
            {
                _vSelf[layer] = output.IntoHalf();
            }
        }

        return logits ?? throw new InvalidOperationException("Whisper decoder returned no logits.");
    }

    public void Dispose()
    {
        _decoder.Dispose();
        _encoder.Dispose();
    }

    private static (char Kind, int Layer) ParseCrossName(string name) =>
        (name[0], name[^1] - '0');

    private static OrtTensorSpec Spec(string name, OrtTensorDataKind kind, params long[] dims) =>
        new() { Name = name, ElementType = kind, Dimensions = dims };

    internal static OrtSessionContract EncoderContract { get; } = new()
    {
        Inputs = [Spec("input_features", OrtTensorDataKind.Float16, 1, 128, 3000)],
        Outputs = Enumerable.Range(0, Layers).SelectMany(i => new[]
        {
            Spec($"k_cache_cross_{i}", OrtTensorDataKind.Float16, KCrossShape),
            Spec($"v_cache_cross_{i}", OrtTensorDataKind.Float16, VCrossShape),
        }).ToList(),
    };

    internal static OrtSessionContract DecoderContract { get; } = new()
    {
        Inputs = new[]
        {
            Spec("input_ids", OrtTensorDataKind.Int32, 1, 1),
            Spec("position_ids", OrtTensorDataKind.Int32, 1),
        }.Concat(Enumerable.Range(0, Layers).SelectMany(i => new[]
        {
            Spec($"k_cache_self_{i}_in", OrtTensorDataKind.Float16, KSelfShape),
            Spec($"v_cache_self_{i}_in", OrtTensorDataKind.Float16, VSelfShape),
        }.Concat(i == 0 ? [Spec("attention_mask", OrtTensorDataKind.Float16, 1, 1, 1, CtxLength)] : [])
         .Concat(
        [
            Spec($"k_cache_cross_{i}", OrtTensorDataKind.Float16, KCrossShape),
            Spec($"v_cache_cross_{i}", OrtTensorDataKind.Float16, VCrossShape),
        ]))).ToList(),
        Outputs = Enumerable.Range(0, Layers).SelectMany(i => new[]
        {
            Spec($"k_cache_self_{i}_out", OrtTensorDataKind.Float16, KSelfShape),
            Spec($"v_cache_self_{i}_out", OrtTensorDataKind.Float16, VSelfShape),
        }).Append(Spec("logits", OrtTensorDataKind.Float16, 1, VocabSize, 1, 1)).ToList(),
    };
}
