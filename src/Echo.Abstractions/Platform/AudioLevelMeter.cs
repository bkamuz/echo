namespace echo.Abstractions.Platform;

/// <summary>Builds a throttled speech spectrum from PCM for the cursor spectrogram.</summary>
public sealed class AudioLevelMeter
{
    public const int BandCount = 24;

    private const int FftSize = 256;
    private const long ThrottleMs = 16; // keeps spectrogram scroll ~1 column / 16–20 ms
    private const float NoiseFloor = 0.004f;
    private const float PeakAttack = 0.9f;
    private const float PeakRelease = 0.035f;
    private const float MinHz = 80f;
    private const float MaxHz = 4000f;

    private readonly float[] _ring = new float[FftSize];
    private readonly float[] _re = new float[FftSize];
    private readonly float[] _im = new float[FftSize];
    private readonly float[] _hann = new float[FftSize];
    private readonly float[] _bands = new float[BandCount];
    private readonly float[] _bandPeaks = new float[BandCount];
    private readonly int[] _bandBinStart = new int[BandCount];
    private readonly int[] _bandBinEnd = new int[BandCount];

    private int _ringWrite;
    private int _ringCount;
    private long _lastRaiseMs;
    private int _sampleRate = 16000;

    public AudioLevelMeter()
    {
        for (var i = 0; i < FftSize; i++)
        {
            _hann[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FftSize - 1)));
        }

        RebuildBandMap();
    }

    public event EventHandler<float[]>? SpectrumChanged;

    public void Configure(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            return;
        }

        _sampleRate = sampleRate;
        RebuildBandMap();
    }

    public void ReportPcm16Le(ReadOnlySpan<byte> bytes)
    {
        for (var offset = 0; offset + 1 < bytes.Length; offset += 2)
        {
            var sample = (short)(bytes[offset] | (bytes[offset + 1] << 8));
            AppendSample(sample / (float)short.MaxValue);
        }

        MaybePublish();
    }

    public void ReportSamples(ReadOnlySpan<float> samples)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            AppendSample(samples[i]);
        }

        MaybePublish();
    }

    private void AppendSample(float sample)
    {
        _ring[_ringWrite] = sample;
        _ringWrite = (_ringWrite + 1) & (FftSize - 1);
        if (_ringCount < FftSize)
        {
            _ringCount++;
        }
    }

    private void MaybePublish()
    {
        var now = Environment.TickCount64;
        if (now - _lastRaiseMs < ThrottleMs || _ringCount < FftSize)
        {
            return;
        }

        _lastRaiseMs = now;
        ComputeSpectrum();
        SpectrumChanged?.Invoke(this, _bands);
    }

    public void Reset()
    {
        Array.Clear(_ring);
        Array.Clear(_bands);
        Array.Clear(_bandPeaks);
        _ringWrite = 0;
        _ringCount = 0;
        _lastRaiseMs = 0;
        SpectrumChanged?.Invoke(this, _bands);
    }

    private void ComputeSpectrum()
    {
        // Oldest sample first from ring.
        var start = _ringWrite;
        for (var i = 0; i < FftSize; i++)
        {
            var s = _ring[(start + i) & (FftSize - 1)];
            _re[i] = s * _hann[i];
            _im[i] = 0f;
        }

        MiniFft.Forward(_re, _im);

        for (var b = 0; b < BandCount; b++)
        {
            var bin0 = _bandBinStart[b];
            var bin1 = _bandBinEnd[b];
            double sum = 0;
            var count = 0;
            for (var bin = bin0; bin < bin1; bin++)
            {
                var re = _re[bin];
                var im = _im[bin];
                sum += Math.Sqrt(re * re + im * im);
                count++;
            }

            var mag = count > 0 ? (float)(sum / count / FftSize) : 0f;
            // Scale magnitude into a usable 0..1-ish range before AGC.
            mag = Math.Clamp(mag * 8f, 0f, 1f);
            _bands[b] = AdaptBand(mag, ref _bandPeaks[b]);
        }
    }

    private static float AdaptBand(float level, ref float peak)
    {
        level = Math.Clamp(level, 0f, 1f);
        if (level > peak)
        {
            peak += (level - peak) * PeakAttack;
        }
        else
        {
            var floor = Math.Max(level, NoiseFloor * 0.5f);
            peak += (floor - peak) * PeakRelease;
        }

        if (peak < NoiseFloor || level < NoiseFloor)
        {
            return 0f;
        }

        var normalized = Math.Clamp(level / peak, 0f, 1f);
        return MathF.Log10(1f + 9f * normalized);
    }

    private void RebuildBandMap()
    {
        var nyquist = _sampleRate * 0.5f;
        var maxHz = Math.Min(MaxHz, nyquist * 0.95f);
        var minHz = Math.Min(MinHz, maxHz * 0.5f);
        var ratio = MathF.Pow(maxHz / minHz, 1f / BandCount);

        var edges = new float[BandCount + 1];
        edges[0] = minHz;
        for (var i = 1; i <= BandCount; i++)
        {
            edges[i] = edges[i - 1] * ratio;
        }

        for (var b = 0; b < BandCount; b++)
        {
            var start = HzToBin(edges[b]);
            var end = HzToBin(edges[b + 1]);
            if (end <= start)
            {
                end = start + 1;
            }

            _bandBinStart[b] = Math.Clamp(start, 1, FftSize / 2 - 1);
            _bandBinEnd[b] = Math.Clamp(end, _bandBinStart[b] + 1, FftSize / 2);
        }
    }

    private int HzToBin(float hz) =>
        (int)MathF.Round(hz * FftSize / _sampleRate);
}
