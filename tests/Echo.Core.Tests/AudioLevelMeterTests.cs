using echo.Abstractions.Platform;

namespace echo.Core.Tests;

public sealed class AudioLevelMeterTests
{
    [Fact]
    public void ReportPcm16Le_Sine1kHz_MidBandsAboveEdges_SilenceNearZero()
    {
        var bands = DriveSine(amplitude: 0.9f);
        Assert.Equal(AudioLevelMeter.BandCount, bands.Length);

        // 1 kHz falls in the mid log bands for 80 Hz–4 kHz.
        var midStart = bands.Length / 3;
        var midEnd = bands.Length * 2 / 3;
        var midSum = 0f;
        for (var i = midStart; i < midEnd; i++)
        {
            midSum += bands[i];
        }

        var mid = midSum / (midEnd - midStart);
        var edges = (bands[0] + bands[^1]) / 2f;
        Assert.True(mid > edges + 0.05f, $"mid={mid} should exceed edges={edges}");

        float[]? silence = null;
        var quiet = new AudioLevelMeter();
        quiet.Configure(16_000);
        quiet.SpectrumChanged += (_, b) => silence = (float[])b.Clone();
        quiet.ReportPcm16Le(new byte[256 * 2]);
        Assert.NotNull(silence);
        Assert.All(silence!, b => Assert.True(b < 0.05f));
    }

    [Fact]
    public void ReportPcm16Le_QuietSine_StillLightsMidBands()
    {
        // Typical speech peaks are far below 0.9 full-scale; spectrogram must still move.
        var bands = DriveSine(amplitude: 0.05f);
        var midStart = bands.Length / 3;
        var midEnd = bands.Length * 2 / 3;
        var midMax = 0f;
        for (var i = midStart; i < midEnd; i++)
        {
            midMax = Math.Max(midMax, bands[i]);
        }

        Assert.True(midMax > 0.2f, $"quiet-speech midMax={midMax} should stay visible");
    }

    private static float[] DriveSine(float amplitude)
    {
        float[]? bands = null;
        var meter = new AudioLevelMeter();
        meter.Configure(16_000);
        meter.SpectrumChanged += (_, b) => bands = (float[])b.Clone();

        const int fftSamples = 256;
        var pcm = new byte[fftSamples * 2];
        for (var i = 0; i < fftSamples; i++)
        {
            var sample = (short)(MathF.Sin(2f * MathF.PI * 1000f * i / 16_000f) * short.MaxValue * amplitude);
            pcm[i * 2] = (byte)(sample & 0xFF);
            pcm[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }

        meter.ReportPcm16Le(pcm);
        Assert.NotNull(bands);
        return bands!;
    }
}
