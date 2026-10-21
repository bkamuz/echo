namespace echo.Core;

/// <summary>
/// Trims the non-speech tail recorded after the hotkey is released (post-release tail,
/// key clicks, breathing). Transducer models such as Parakeet TDT tend to emit extra
/// words on that tail. Short loud bursts (key clicks) are not treated as speech:
/// speech must stay above the threshold for <see cref="MinSpeechMs"/>.
/// </summary>
public static class SpeechTailTrimmer
{
    public const int FrameMs = 20;
    public const int MinSpeechMs = 120;
    public const int KeepAfterSpeechMs = 300;

    public readonly record struct Result(float[] Samples, int TrimmedMs, bool SpeechFound);

    public static Result TrimTrailing(float[] samples, int sampleRate)
    {
        var frame = Math.Max(1, sampleRate * FrameMs / 1000);
        var frames = samples.Length / frame;
        if (frames < 10)
        {
            return new Result(samples, 0, true);
        }

        var rms = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            double sum = 0;
            var start = f * frame;
            for (var i = 0; i < frame; i++)
            {
                var s = samples[start + i];
                sum += s * s;
            }

            rms[f] = (float)Math.Sqrt(sum / frame);
        }

        var sorted = (float[])rms.Clone();
        Array.Sort(sorted);
        var noiseFloor = sorted[(int)(frames * 0.1)];
        var loud = sorted[(int)(frames * 0.95)];
        if (loud < 0.003f)
        {
            // Whole clip is near-silent — leave the decision to the engine / silence guards.
            return new Result(samples, 0, false);
        }

        // Threshold between noise floor and loud speech; never below an absolute floor.
        var threshold = Math.Max(0.002f, Math.Max(noiseFloor * 3f, noiseFloor + ((loud - noiseFloor) * 0.12f)));
        var minRun = Math.Max(1, MinSpeechMs / FrameMs);

        var lastSpeechEnd = -1;
        var run = 0;
        for (var f = 0; f < frames; f++)
        {
            if (rms[f] >= threshold)
            {
                run++;
                if (run >= minRun)
                {
                    lastSpeechEnd = f + 1;
                }
            }
            else
            {
                // Allow a 1-frame dip inside a syllable.
                if (run > 0 && f + 1 < frames && rms[f + 1] >= threshold)
                {
                    continue;
                }

                run = 0;
            }
        }

        if (lastSpeechEnd < 0)
        {
            return new Result(samples, 0, false);
        }

        var keepFrames = KeepAfterSpeechMs / FrameMs;
        var endSample = Math.Min(samples.Length, (lastSpeechEnd + keepFrames) * frame);
        if (endSample >= samples.Length - frame)
        {
            return new Result(samples, 0, true);
        }

        var trimmed = samples.AsSpan(0, endSample).ToArray();
        var trimmedMs = (int)((samples.Length - endSample) * 1000L / sampleRate);
        return new Result(trimmed, trimmedMs, true);
    }
}
