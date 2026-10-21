using echo.Core;
using echo.Engines.ParakeetNpu.Parakeet;

namespace echo.Core.Tests;

public sealed class SpeechTailTrimmerTests
{
    private const int Rate = 16000;

    private static float[] Build(params (double Seconds, float Amp, bool Tone)[] parts)
    {
        var rng = new Random(7);
        var list = new List<float>();
        foreach (var (sec, amp, tone) in parts)
        {
            var n = (int)(sec * Rate);
            for (var i = 0; i < n; i++)
            {
                list.Add(tone
                    ? amp * (float)Math.Sin(2 * Math.PI * 220 * i / Rate)
                    : amp * (float)(rng.NextDouble() - 0.5));
            }
        }

        return list.ToArray();
    }

    [Fact]
    public void TrimsQuietTailAfterSpeech_KeepsPadding()
    {
        var s = Build((0.3, 0.002f, false), (2.0, 0.3f, true), (1.0, 0.002f, false));
        var r = SpeechTailTrimmer.TrimTrailing(s, Rate);
        Assert.True(r.SpeechFound);
        var keptSec = r.Samples.Length / (double)Rate;
        Assert.InRange(keptSec, 2.5, 2.7);
        Assert.InRange(r.TrimmedMs, 650, 800);
    }

    [Fact]
    public void KeyClickInTail_IsNotTreatedAsSpeech()
    {
        var s = Build((2.0, 0.3f, true), (0.5, 0.002f, false), (0.04, 0.6f, false), (0.5, 0.002f, false));
        var r = SpeechTailTrimmer.TrimTrailing(s, Rate);
        Assert.InRange(r.Samples.Length / (double)Rate, 2.2, 2.4);
    }

    [Fact]
    public void SpeechUntilEnd_Untouched()
    {
        var s = Build((0.2, 0.002f, false), (2.0, 0.3f, true));
        var r = SpeechTailTrimmer.TrimTrailing(s, Rate);
        Assert.Equal(0, r.TrimmedMs);
        Assert.Same(s, r.Samples);
    }

    [Fact]
    public void AllSilent_LeftForEngine()
    {
        var s = Build((2.0, 0.001f, false));
        var r = SpeechTailTrimmer.TrimTrailing(s, Rate);
        Assert.False(r.SpeechFound);
        Assert.Same(s, r.Samples);
    }

    [Fact]
    public void TooShort_Untouched()
    {
        var s = new float[100];
        Assert.Same(s, SpeechTailTrimmer.TrimTrailing(s, Rate).Samples);
    }

    [Fact]
    public void QuietSpeakerWithNoisyRoom_StillKeepsSpeech()
    {
        var s = Build((0.5, 0.02f, false), (1.5, 0.08f, true), (1.0, 0.02f, false));
        var r = SpeechTailTrimmer.TrimTrailing(s, Rate);
        Assert.InRange(r.Samples.Length / (double)Rate, 2.2, 2.4);
    }
}

public sealed class ParakeetTailGuardTests
{
    [Theory]
    [InlineData("привет как дела дела дела", "привет как дела")]
    [InlineData("ну да да", "ну да да")]
    [InlineData("отправь отчёт сегодня отчёт сегодня", "отправь отчёт сегодня")]
    [InlineData("hello world", "hello world")]
    [InlineData("один два три", "один два три")]
    public void CollapseRepeatedTail(string input, string expected) =>
        Assert.Equal(expected, ParakeetPipeline.CollapseRepeatedTail(input));

    [Fact]
    public void Merge_ExactOverlap()
    {
        Assert.Equal(
            "мы проверяем распознавание длинной речи",
            ParakeetPipeline.MergeOverlappingChunks("мы проверяем распознавание", "распознавание длинной речи"));
    }

    [Fact]
    public void Merge_TruncatedBoundaryWord_DoesNotDuplicate()
    {
        Assert.Equal(
            "мы проверяем распознавание длинной речи",
            ParakeetPipeline.MergeOverlappingChunks("мы проверяем распозна", "проверяем распознавание длинной речи"));
    }

    [Fact]
    public void Merge_NoOverlap_Concatenates()
    {
        Assert.Equal("a b c d", ParakeetPipeline.MergeOverlappingChunks("a b", "c d"));
    }
}
