using echo.Abstractions.Platform;

namespace echo.Core.Tests;

public sealed class AudioLevelMeterTests
{
    [Fact]
    public void ReportPcm16Le_Sine1kHz_MidBandsAboveEdges_SilenceNearZero()
    {
        float[]? bands = null;
        var meter = new AudioLevelMeter();
        meter.Configure(16_000);
        meter.SpectrumChanged += (_, b) => bands = (float[])b.Clone();

        const int fftSamples = 256;
        var pcm = new byte[fftSamples * 2];
        for (var i = 0; i < fftSamples; i++)
        {
            var sample = (short)(MathF.Sin(2f * MathF.PI * 1000f * i / 16_000f) * short.MaxValue * 0.9f);
            pcm[i * 2] = (byte)(sample & 0xFF);
            pcm[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }

        meter.ReportPcm16Le(pcm);
        Assert.NotNull(bands);
        Assert.Equal(AudioLevelMeter.BandCount, bands!.Length);

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

        bands = null;
        var quiet = new AudioLevelMeter();
        quiet.Configure(16_000);
        quiet.SpectrumChanged += (_, b) => bands = (float[])b.Clone();
        quiet.ReportPcm16Le(new byte[fftSamples * 2]);
        Assert.NotNull(bands);
        Assert.All(bands!, b => Assert.True(b < 0.05f));
    }
}
