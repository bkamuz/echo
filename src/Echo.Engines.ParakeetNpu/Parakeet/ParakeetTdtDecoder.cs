using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace echo.Engines.ParakeetNpu.Parakeet;

/// <summary>
/// TDT greedy decoder for Parakeet (matches istupakov/onnx-asr loop).
/// </summary>
public sealed class ParakeetTdtDecoder
{
    private const int MaxTokensPerStep = 10;

    private readonly int _vocabSize;
    private readonly int _blankId;
    private readonly (int Dim0, int Dim1, int Dim2) _state1Shape;
    private readonly (int Dim0, int Dim1, int Dim2) _state2Shape;

    public ParakeetTdtDecoder(InferenceSession decoderJoint, int vocabSize, int blankId)
    {
        _vocabSize = vocabSize;
        _blankId = blankId;
        _state1Shape = FindShape(decoderJoint, "input_states_1");
        _state2Shape = FindShape(decoderJoint, "input_states_2");
    }

    public IReadOnlyList<int> Decode(
        InferenceSession decoderJoint,
        float[] encoderOut,
        int batch,
        int timeSteps,
        int featureDim,
        int encoderOutLen)
    {
        var state1 = new float[Volume(_state1Shape)];
        var state2 = new float[Volume(_state2Shape)];
        var tokens = new List<int>();
        var t = 0;
        var emittedThisStep = 0;

        while (t < encoderOutLen)
        {
            var encFrame = ExtractFrame(encoderOut, batch, timeSteps, featureDim, t);
            var prevToken = tokens.Count > 0 ? tokens[^1] : _blankId;

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(
                    "encoder_outputs",
                    new DenseTensor<float>(encFrame, [batch, featureDim, 1])),
                NamedOnnxValue.CreateFromTensor(
                    "targets",
                    new DenseTensor<long>(new long[] { prevToken }, new int[] { batch, 1 })),
                NamedOnnxValue.CreateFromTensor(
                    "target_length",
                    new DenseTensor<long>(new long[] { 1 }, new int[] { 1 })),
                NamedOnnxValue.CreateFromTensor(
                    "input_states_1",
                    new DenseTensor<float>(state1, [_state1Shape.Dim0, _state1Shape.Dim1, _state1Shape.Dim2])),
                NamedOnnxValue.CreateFromTensor(
                    "input_states_2",
                    new DenseTensor<float>(state2, [_state2Shape.Dim0, _state2Shape.Dim1, _state2Shape.Dim2])),
            };

            using var results = decoderJoint.Run(inputs);
            var logits = results.First(r => r.Name == "outputs").AsEnumerable<float>().ToArray();
            var nextState1 = results.First(r => r.Name == "output_states_1").AsTensor<float>().ToArray();
            var nextState2 = results.First(r => r.Name == "output_states_2").AsTensor<float>().ToArray();

            if (logits.Length < _vocabSize)
            {
                throw new InvalidOperationException(
                    $"Joint logits length {logits.Length} is smaller than vocab size {_vocabSize}.");
            }

            var token = ArgMax(logits.AsSpan(0, _vocabSize));
            var step = logits.Length > _vocabSize
                ? ArgMax(logits.AsSpan(_vocabSize))
                : 1;

            if (token != _blankId)
            {
                tokens.Add(token);
                Array.Copy(nextState1, state1, nextState1.Length);
                Array.Copy(nextState2, state2, nextState2.Length);
                emittedThisStep++;
            }

            if (step > 0)
            {
                t += step;
                emittedThisStep = 0;
            }
            else if (token == _blankId || emittedThisStep >= MaxTokensPerStep)
            {
                t++;
                emittedThisStep = 0;
            }
        }

        return tokens;
    }

    private static float[] ExtractFrame(float[] encoderOut, int batch, int timeSteps, int featureDim, int t)
    {
        var frame = new float[batch * featureDim];
        for (var b = 0; b < batch; b++)
        {
            for (var d = 0; d < featureDim; d++)
            {
                frame[(b * featureDim) + d] = encoderOut[((b * timeSteps) + t) * featureDim + d];
            }
        }

        return frame;
    }

    private static int ArgMax(ReadOnlySpan<float> values)
    {
        var bestIndex = 0;
        var bestValue = float.NegativeInfinity;
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] > bestValue)
            {
                bestValue = values[i];
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static (int Dim0, int Dim1, int Dim2) FindShape(InferenceSession session, string name)
    {
        var input = session.InputMetadata.FirstOrDefault(i => i.Key == name).Value
            ?? throw new InvalidOperationException($"Decoder input '{name}' is missing.");
        var dims = input.Dimensions;
        if (dims.Length < 3)
        {
            throw new InvalidOperationException($"Decoder input '{name}' rank {dims.Length} != 3.");
        }

        return (
            Math.Max(dims[0], 1),
            Math.Max(dims[1], 1),
            Math.Max(dims[2], 1));
    }

    private static int Volume((int Dim0, int Dim1, int Dim2) shape) =>
        shape.Dim0 * shape.Dim1 * shape.Dim2;
}
