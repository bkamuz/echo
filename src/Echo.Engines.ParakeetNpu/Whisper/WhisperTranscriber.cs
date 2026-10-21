using Microsoft.Extensions.Logging;

namespace echo.Engines.ParakeetNpu.Whisper;

/// <summary>Splits 16 kHz audio into 30 s windows, runs mel → encoder → greedy decoder, joins text.</summary>
public sealed class WhisperTranscriber
{
    /// <summary>Windows shorter than this (trailing remainder) are skipped.</summary>
    public const int MinWindowSamples = WhisperMel.SampleRate / 10;

    private readonly IWhisperModelRunner _runner;
    private readonly WhisperTokenizer _tokenizer;
    private readonly WhisperGreedyDecoder _decoder;
    private readonly ILogger? _logger;

    public WhisperTranscriber(IWhisperModelRunner runner, WhisperTokenizer tokenizer, ILogger? logger = null)
    {
        _runner = runner;
        _tokenizer = tokenizer;
        _logger = logger;
        _decoder = new WhisperGreedyDecoder(logger);
    }

    public static IEnumerable<(int Offset, int Length)> Windows(int sampleCount)
    {
        for (var offset = 0; offset < sampleCount; offset += WhisperMel.WindowSamples)
        {
            var length = Math.Min(WhisperMel.WindowSamples, sampleCount - offset);
            if (length >= MinWindowSamples || offset == 0)
            {
                yield return (offset, length);
            }
        }
    }

    public string Transcribe(float[] samples16k, string? language, CancellationToken cancellationToken = default)
    {
        var languageToken = WhisperTokens.LanguageToken(language);
        var parts = new List<string>();
        var index = 0;
        foreach (var (offset, length) in Windows(samples16k.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var mel = WhisperMel.Compute(samples16k.AsSpan(offset, length));
            var melMs = sw.ElapsedMilliseconds;
            var result = _decoder.DecodeWindow(_runner, WhisperMel.ToHalf(mel), languageToken);
            var text = _tokenizer.Decode(result.Tokens).Trim();
            _logger?.LogInformation(
                "Whisper NPU window {Index}: {Seconds:F1}s audio, mel {MelMs} ms, total {TotalMs} ms, steps={Steps}, tokens={Tokens}, lang={Language}, chars={Chars}",
                index,
                length / (double)WhisperMel.SampleRate,
                melMs,
                sw.ElapsedMilliseconds,
                result.Steps,
                result.Tokens.Count,
                result.Language ?? "?",
                text.Length);
            if (text.Length > 0)
            {
                parts.Add(text);
            }

            index++;
        }

        return string.Join(' ', parts);
    }

    public static float[] ResampleTo16k(float[] samples, int sampleRate)
    {
        if (sampleRate == WhisperMel.SampleRate || samples.Length == 0)
        {
            return samples;
        }

        var ratio = (double)sampleRate / WhisperMel.SampleRate;
        var length = (int)(samples.Length / ratio);
        var result = new float[length];
        for (var i = 0; i < length; i++)
        {
            var pos = i * ratio;
            var i0 = (int)pos;
            var i1 = Math.Min(i0 + 1, samples.Length - 1);
            var frac = (float)(pos - i0);
            result[i] = samples[i0] * (1 - frac) + samples[i1] * frac;
        }

        return result;
    }
}
