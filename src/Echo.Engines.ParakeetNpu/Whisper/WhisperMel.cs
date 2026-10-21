namespace echo.Engines.ParakeetNpu.Whisper;

/// <summary>
/// Log-mel features identical to HF <c>WhisperFeatureExtractor</c> (numpy path): 16 kHz, 30 s window
/// (480000 samples, zero padded), n_fft 400, hop 160, periodic Hann, centered reflect padding,
/// power spectrum, 128 Slaney mel filters (0–8000 Hz, Slaney norm), log10 with 1e-10 floor,
/// drop last frame (3000 frames), clamp to max−8, then (x+4)/4.
/// Output layout is [mel][frame] = [128, 3000] row-major, matching <c>input_features</c> [1,128,3000].
/// </summary>
public static class WhisperMel
{
    public const int SampleRate = 16000;
    public const int NFft = 400;
    public const int HopLength = 160;
    public const int MelBins = 128;
    public const int Frames = 3000;
    public const int WindowSamples = 480000;
    public const int FreqBins = NFft / 2 + 1;

    private static readonly Lazy<float[]> Window = new(CreateHann);
    private static readonly Lazy<float[,]> Filters = new(() => CreateMelFilters(MelBins));
    private static readonly Lazy<(float[] Cos, float[] Sin)> Twiddles = new(CreateTwiddles);

    public static float[] Compute(ReadOnlySpan<float> samples)
    {
        var padded = new float[WindowSamples];
        samples[..Math.Min(samples.Length, WindowSamples)].CopyTo(padded);

        const int pad = NFft / 2;
        var totalFrames = 1 + WindowSamples / HopLength; // 3001, last one dropped
        var window = Window.Value;
        var (cos, sin) = Twiddles.Value;
        var filters = Filters.Value;

        var mel = new float[MelBins * Frames];
        var frame = new float[NFft];
        var power = new double[FreqBins];
        for (var t = 0; t < totalFrames - 1; t++)
        {
            var start = t * HopLength - pad;
            for (var i = 0; i < NFft; i++)
            {
                frame[i] = ReflectAt(padded, start + i) * window[i];
            }

            for (var k = 0; k < FreqBins; k++)
            {
                double re = 0, im = 0;
                var row = k * NFft;
                for (var n = 0; n < NFft; n++)
                {
                    re += frame[n] * cos[row + n];
                    im -= frame[n] * sin[row + n];
                }

                power[k] = re * re + im * im;
            }

            for (var m = 0; m < MelBins; m++)
            {
                double acc = 0;
                for (var k = 0; k < FreqBins; k++)
                {
                    var w = filters[m, k];
                    if (w != 0f)
                    {
                        acc += w * power[k];
                    }
                }

                mel[m * Frames + t] = (float)Math.Log10(Math.Max(acc, 1e-10));
            }
        }

        var max = float.NegativeInfinity;
        foreach (var v in mel)
        {
            if (v > max)
            {
                max = v;
            }
        }

        var floor = max - 8f;
        for (var i = 0; i < mel.Length; i++)
        {
            mel[i] = (Math.Max(mel[i], floor) + 4f) / 4f;
        }

        return mel;
    }

    public static Half[] ToHalf(float[] values)
    {
        var result = new Half[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            result[i] = (Half)values[i];
        }

        return result;
    }

    private static float ReflectAt(float[] x, int index)
    {
        var n = x.Length;
        if (index < 0)
        {
            index = -index;
        }

        if (index >= n)
        {
            index = 2 * (n - 1) - index;
        }

        return x[index];
    }

    private static float[] CreateHann()
    {
        // Periodic Hann (numpy/HF window_function(400, "hann", periodic=True)).
        var w = new float[NFft];
        for (var i = 0; i < NFft; i++)
        {
            w[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / NFft));
        }

        return w;
    }

    private static (float[] Cos, float[] Sin) CreateTwiddles()
    {
        var c = new float[FreqBins * NFft];
        var s = new float[FreqBins * NFft];
        for (var k = 0; k < FreqBins; k++)
        {
            for (var n = 0; n < NFft; n++)
            {
                var angle = 2 * Math.PI * ((long)k * n % NFft) / NFft;
                c[k * NFft + n] = (float)Math.Cos(angle);
                s[k * NFft + n] = (float)Math.Sin(angle);
            }
        }

        return (c, s);
    }

    private static double HzToMel(double hz)
    {
        const double minLogHz = 1000.0;
        const double minLogMel = 15.0;
        var logstep = 27.0 / Math.Log(6.4);
        return hz >= minLogHz ? minLogMel + Math.Log(hz / minLogHz) * logstep : 3.0 * hz / 200.0;
    }

    private static double MelToHz(double mel)
    {
        const double minLogHz = 1000.0;
        const double minLogMel = 15.0;
        var logstep = Math.Log(6.4) / 27.0;
        return mel >= minLogMel ? minLogHz * Math.Exp(logstep * (mel - minLogMel)) : 200.0 * mel / 3.0;
    }

    internal static float[,] CreateMelFilters(int melBins)
    {
        var melMin = HzToMel(0);
        var melMax = HzToMel(SampleRate / 2.0);
        var filterFreqs = new double[melBins + 2];
        for (var i = 0; i < filterFreqs.Length; i++)
        {
            filterFreqs[i] = MelToHz(melMin + (melMax - melMin) * i / (melBins + 1));
        }

        var fftFreqs = new double[FreqBins];
        for (var k = 0; k < FreqBins; k++)
        {
            fftFreqs[k] = (SampleRate / 2.0) * k / (FreqBins - 1);
        }

        var filters = new float[melBins, FreqBins];
        for (var m = 0; m < melBins; m++)
        {
            var enorm = 2.0 / (filterFreqs[m + 2] - filterFreqs[m]);
            for (var k = 0; k < FreqBins; k++)
            {
                var down = (fftFreqs[k] - filterFreqs[m]) / (filterFreqs[m + 1] - filterFreqs[m]);
                var up = (filterFreqs[m + 2] - fftFreqs[k]) / (filterFreqs[m + 2] - filterFreqs[m + 1]);
                filters[m, k] = (float)(Math.Max(0, Math.Min(down, up)) * enorm);
            }
        }

        return filters;
    }
}
