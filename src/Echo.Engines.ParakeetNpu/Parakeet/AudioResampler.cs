namespace echo.Engines.ParakeetNpu.Parakeet;

public static class AudioResampler
{
    public const int TargetSampleRate = ParakeetPipeline.SampleRate;

    /// <summary>Linear resample any f32 mono buffer to 16 kHz mono.</summary>
    public static float[] To16kMono(IReadOnlyList<float> input, int inputSampleRate)
    {
        if (input.Count == 0)
        {
            return [];
        }

        if (inputSampleRate == TargetSampleRate)
        {
            return input.ToArray();
        }

        if (inputSampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputSampleRate));
        }

        var ratio = (double)TargetSampleRate / inputSampleRate;
        var outputLength = Math.Max(1, (int)Math.Round(input.Count * ratio));
        var output = new float[outputLength];
        var lastIndex = input.Count - 1;

        for (var i = 0; i < outputLength; i++)
        {
            var sourcePosition = i / ratio;
            var leftIndex = (int)Math.Floor(sourcePosition);
            var rightIndex = Math.Min(leftIndex + 1, lastIndex);
            var fraction = sourcePosition - leftIndex;
            output[i] = (float)(input[leftIndex] * (1.0 - fraction) + input[rightIndex] * fraction);
        }

        return output;
    }
}
