using echo.Engines.ParakeetNpu.Ort;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace echo.Engines.ParakeetNpu.Parakeet;

public sealed class ParakeetPipeline : IDisposable
{
    public const float MaxNpuSeconds = 8f;
    public const int SampleRate = 16000;

    private readonly InferenceSession _preprocessor;
    private readonly InferenceSession _decoderJoint;
    private readonly InferenceSession? _cpuEncoder;
    private readonly OrtQnnSession? _npuEncoder;
    private readonly ParakeetVocab _vocab;
    private readonly ParakeetTdtDecoder _tdtDecoder;
    private readonly bool _useNpu;

    private ParakeetPipeline(
        InferenceSession preprocessor,
        InferenceSession decoderJoint,
        InferenceSession? cpuEncoder,
        OrtQnnSession? npuEncoder,
        ParakeetVocab vocab,
        ParakeetTdtDecoder tdtDecoder,
        bool useNpu)
    {
        _preprocessor = preprocessor;
        _decoderJoint = decoderJoint;
        _cpuEncoder = cpuEncoder;
        _npuEncoder = npuEncoder;
        _vocab = vocab;
        _tdtDecoder = tdtDecoder;
        _useNpu = useNpu;
    }

    public static ParakeetPipeline LoadCpu(string modelDir, ILogger? logger = null)
    {
        QnnRuntimePaths.PrepareNativeSearchPath();
        var preprocessor = CreateCpuSession(Path.Combine(modelDir, "nemo128.onnx"));
        var decoderJoint = CreateCpuSession(Path.Combine(modelDir, "decoder_joint-model.int8.onnx"));
        var encoder = CreateCpuSession(Path.Combine(modelDir, "encoder-model.int8.onnx"));
        var vocab = ParakeetVocab.Load(Path.Combine(modelDir, "vocab.txt"));
        logger?.LogInformation("Parakeet CPU pipeline loaded from {ModelDir}", modelDir);

        return new ParakeetPipeline(
            preprocessor,
            decoderJoint,
            encoder,
            null,
            vocab,
            new ParakeetTdtDecoder(decoderJoint, vocab.Size, vocab.BlankId),
            useNpu: false);
    }

    public static ParakeetPipeline LoadNpu(string modelDir, string runtimeDir, ILogger? logger = null)
    {
        logger?.LogInformation("Initializing ORT/QNN runtime from {RuntimeDir}", runtimeDir);
        OrtQnnBootstrap.Initialize(runtimeDir, logger);

        logger?.LogInformation("Acquiring QNN execution provider lease");
        var providerLease = OrtQnnBootstrap.AcquireQnnProvider(logger);

        logger?.LogInformation("Enumerating QNN NPU devices");
        var npuDevices = OrtQnnBootstrap.EnumerateQnnNpuDevices(logger);
        if (npuDevices.Count == 0)
        {
            providerLease.Dispose();
            throw new InvalidOperationException("No QNN NPU device discovered by ORT.");
        }

        var preprocessor = CreateCpuSession(Path.Combine(modelDir, "nemo128.onnx"));
        var decoderJoint = CreateCpuSession(Path.Combine(modelDir, "decoder_joint-model.int8.onnx"));
        var vocab = ParakeetVocab.Load(Path.Combine(modelDir, "vocab.txt"));

        OrtQnnSession npuEncoder;
        try
        {
            npuEncoder = OrtQnnSession.Load(
                Path.Combine(modelDir, "encoder-model.onnx"),
                npuDevices,
                OrtSessionContract.ParakeetEncoder,
                providerLease,
                logger);
        }
        catch
        {
            preprocessor.Dispose();
            decoderJoint.Dispose();
            providerLease.Dispose();
            throw;
        }

        logger?.LogInformation(
            "Parakeet NPU pipeline loaded from {ModelDir} with {DeviceCount} QNN NPU device(s)",
            modelDir,
            npuDevices.Count);

        return new ParakeetPipeline(
            preprocessor,
            decoderJoint,
            cpuEncoder: null,
            npuEncoder,
            vocab,
            new ParakeetTdtDecoder(decoderJoint, vocab.Size, vocab.BlankId),
            useNpu: true);
    }

    public string Transcribe(IReadOnlyList<float> samples, int sampleRate, ILogger? logger = null)
    {
        if (samples.Count == 0)
        {
            return string.Empty;
        }

        var pcm = AudioResampler.To16kMono(samples, sampleRate);
        if (pcm.Length == 0)
        {
            return string.Empty;
        }

        return RunPipeline(pcm, logger);
    }

    private string RunPipeline(float[] pcm16k, ILogger? logger)
    {
        if (!_useNpu)
        {
            var (encoderOut, validT) = PreprocessAndEncode(pcm16k, pcm16k.Length);
            return DecodeFeatures(encoderOut, validT);
        }

        var chunkSamples = (int)(MaxNpuSeconds * SampleRate);
        var strideSamples = (int)((MaxNpuSeconds - 1f) * SampleRate);
        var realSamples = pcm16k.Length;

        if (realSamples <= chunkSamples)
        {
            var padded = PadToLength(pcm16k, chunkSamples);
            var (encoderOut, validT) = PreprocessAndEncode(padded, realSamples);
            return DecodeFeatures(encoderOut, validT);
        }

        var chunkTranscripts = new List<string>();
        var chunkStart = 0;
        var chunkCount = 0;
        while (true)
        {
            var chunkEndReal = Math.Min(chunkStart + chunkSamples, realSamples);
            var chunk = PadToLength(pcm16k.AsSpan(chunkStart, chunkEndReal - chunkStart).ToArray(), chunkSamples);
            var validSamples = chunkEndReal - chunkStart;
            var (encoderOut, validT) = PreprocessAndEncode(chunk, validSamples);
            chunkTranscripts.Add(DecodeFeatures(encoderOut, validT));

            if (chunkEndReal == realSamples)
            {
                break;
            }

            chunkStart += strideSamples;
            chunkCount++;
        }

        logger?.LogInformation("NPU chunked decode across {ChunkCount} windows", chunkCount + 1);
        return chunkTranscripts.Aggregate(string.Empty, MergeOverlappingChunks);
    }

    private (float[] EncoderOut, int ValidT) PreprocessAndEncode(float[] pcm, int validSamples)
    {
        if (validSamples > pcm.Length)
        {
            throw new InvalidOperationException(
                $"Valid sample count {validSamples} exceeds buffer length {pcm.Length}.");
        }

        var waveforms = new DenseTensor<float>(pcm, [1, pcm.Length]);
        var waveformsLens = new DenseTensor<long>(new[] { (long)validSamples }, new[] { 1 });
        using var preOut = _preprocessor.Run([
            NamedOnnxValue.CreateFromTensor("waveforms", waveforms),
            NamedOnnxValue.CreateFromTensor("waveforms_lens", waveformsLens),
        ]);

        var features = preOut.First(r => r.Name == "features").AsTensor<float>();
        var featuresLens = preOut.First(r => r.Name == "features_lens").AsEnumerable<long>().ToArray();
        var featureFrames = features.Dimensions[2];
        var validFeatureFrames = (int)Math.Clamp(featuresLens.FirstOrDefault(), 0, featureFrames);

        if (_useNpu)
        {
            return EncodeNpu(features, featuresLens);
        }

        return EncodeCpu(features, featuresLens, validFeatureFrames, featureFrames);
    }

    private (float[] EncoderOut, int ValidT) EncodeCpu(
        Tensor<float> features,
        long[] featuresLens,
        int validFeatureFrames,
        int featureFrames)
    {
        if (_cpuEncoder is null)
        {
            throw new InvalidOperationException("CPU encoder session is not loaded.");
        }

        using var encOut = _cpuEncoder.Run([
            NamedOnnxValue.CreateFromTensor("audio_signal", features),
            NamedOnnxValue.CreateFromTensor("length", new DenseTensor<long>(featuresLens, new[] { 1 })),
        ]);

        var encoderOut = encOut.First(r => r.Name == "outputs").AsTensor<float>().ToArray();
        var encodedLengths = encOut.First(r => r.Name == "encoded_lengths").AsEnumerable<long>().ToArray();
        var batch = features.Dimensions[0];
        var channels = features.Dimensions[1];
        var timeSteps = encoderOut.Length / (batch * channels);
        var validT = ScaleValidFrames(validFeatureFrames, featureFrames, timeSteps);
        if (encodedLengths.Length > 0)
        {
            validT = Math.Min(validT, (int)Math.Clamp(encodedLengths[0], 0, timeSteps));
        }

        return (encoderOut, validT);
    }

    private (float[] EncoderOut, int ValidT) EncodeNpu(Tensor<float> features, long[] featuresLens)
    {
        if (_npuEncoder is null)
        {
            throw new InvalidOperationException("NPU encoder session is not loaded.");
        }

        var length = checked((int)featuresLens[0]);
        var dimensions = new long[]
        {
            features.Dimensions[0],
            features.Dimensions[1],
            features.Dimensions[2],
        };

        var outputs = _npuEncoder.Run([
            new OrtTensorInput("audio_signal", dimensions, features.ToArray()),
            new OrtTensorInput("length", [1], [length]),
        ]);

        var (outputDims, outputValues) = outputs[0].IntoFloat();
        if (outputDims.Length != 3)
        {
            throw new InvalidOperationException($"NPU encoder output rank mismatch: {outputDims.Length}.");
        }

        var (_, encodedLengths) = outputs[1].IntoInt32();
        var encodedLen = encodedLengths.Length > 0 ? encodedLengths[0] : outputDims[2];
        var validT = Math.Min((int)outputDims[2], (int)encodedLen);
        return (outputValues, validT);
    }

    private string DecodeFeatures(float[] encoderOut, int validT)
    {
        var batch = 1;
        var channels = 1024;
        var timeSteps = encoderOut.Length / (batch * channels);
        var permuted = Permute1024Time(encoderOut, batch, channels, timeSteps);
        var tokens = _tdtDecoder.Decode(_decoderJoint, permuted, batch, timeSteps, channels, validT);
        return _vocab.Detokenize(tokens);
    }

    private static float[] Permute1024Time(float[] encoderOut, int batch, int channels, int timeSteps)
    {
        var permuted = new float[encoderOut.Length];
        for (var b = 0; b < batch; b++)
        {
            for (var t = 0; t < timeSteps; t++)
            {
                for (var c = 0; c < channels; c++)
                {
                    permuted[((b * timeSteps) + t) * channels + c] =
                        encoderOut[((b * channels) + c) * timeSteps + t];
                }
            }
        }

        return permuted;
    }

    private static float[] PadToLength(float[] pcm, int length)
    {
        if (pcm.Length >= length)
        {
            return pcm;
        }

        var padded = new float[length];
        Array.Copy(pcm, padded, pcm.Length);
        return padded;
    }

    private static int ScaleValidFrames(int validInput, int totalInput, int totalOutput)
    {
        if (totalInput == 0 || validInput == 0 || totalOutput == 0)
        {
            return 0;
        }

        var numerator = totalOutput * Math.Min(validInput, totalInput);
        return Math.Min((numerator + totalInput - 1) / totalInput, totalOutput);
    }

    internal static string MergeOverlappingChunks(string transcript, string nextChunk)
    {
        transcript = transcript.Trim();
        nextChunk = nextChunk.Trim();
        if (transcript.Length == 0)
        {
            return nextChunk;
        }

        if (nextChunk.Length == 0)
        {
            return transcript;
        }

        var transcriptWords = transcript.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var nextWords = nextChunk.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var transcriptNormalized = transcriptWords.Select(NormalizeMergeWord).ToArray();
        var nextNormalized = nextWords.Select(NormalizeMergeWord).ToArray();
        var maxOverlap = Math.Min(transcriptWords.Length, nextWords.Length);

        for (var overlap = maxOverlap; overlap >= 1; overlap--)
        {
            var maxTrailing = Math.Min(6, transcriptWords.Length - overlap);
            for (var trailingWords = 0; trailingWords <= maxTrailing; trailingWords++)
            {
                var transcriptEnd = transcriptWords.Length - trailingWords;
                var transcriptStart = transcriptEnd - overlap;
                if (transcriptStart < 0)
                {
                    continue;
                }

                var left = transcriptNormalized.AsSpan(transcriptStart, overlap);
                var right = nextNormalized.AsSpan(0, overlap);
                if (left.SequenceEqual(right) && left.ToArray().All(word => word.Length > 0))
                {
                    var merged = string.Join(' ', transcriptWords.AsSpan(0, transcriptEnd).ToArray());
                    if (overlap < nextWords.Length)
                    {
                        merged += " " + string.Join(' ', nextWords.AsSpan(overlap).ToArray());
                    }

                    return merged;
                }
            }
        }

        return $"{transcript} {nextChunk}";
    }

    private static string NormalizeMergeWord(string word) =>
        new(word.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static InferenceSession CreateCpuSession(string path)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = Math.Min(Math.Max(Environment.ProcessorCount, 1), 8),
        };
        options.AppendExecutionProvider_CPU();
        return new InferenceSession(path, options);
    }

    public void Dispose()
    {
        _npuEncoder?.Dispose();
        _cpuEncoder?.Dispose();
        _decoderJoint.Dispose();
        _preprocessor.Dispose();
    }
}
