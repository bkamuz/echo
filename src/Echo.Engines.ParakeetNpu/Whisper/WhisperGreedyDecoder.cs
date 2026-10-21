using Microsoft.Extensions.Logging;

namespace echo.Engines.ParakeetNpu.Whisper;

/// <summary>Encoder + static-shape KV-cache decoder (AI Hub Whisper export). Swappable for tests.</summary>
public interface IWhisperModelRunner
{
    /// <summary>Decoder context length (attention_mask width); AI Hub turbo export uses 200.</summary>
    int DecodeLength { get; }

    /// <summary>Runs the encoder on [128,3000] fp16 features and resets the self-attention KV cache.</summary>
    void Encode(Half[] features);

    /// <summary>One decoder step; feeds back the self KV cache internally. Returns logits over the vocab.</summary>
    float[] Decode(int tokenId, int position, Half[] attentionMask);
}

public sealed record WhisperWindowResult(IReadOnlyList<int> Tokens, string? Language, int Steps);

/// <summary>
/// Greedy decoding loop mirroring qai_hub_models hf_whisper app: attention mask starts at −100 and
/// position n unmasks slot (L−n−1); position_ids = n; prefix tokens are forced one per step.
/// Prefix: SOT, language (forced or detected from step-0 logits), transcribe, notimestamps.
/// </summary>
public sealed class WhisperGreedyDecoder
{
    public const float MaskNeg = -100f;
    public const int MaxRepeatPeriod = 8;
    public const int MinRepeats = 6;

    private readonly ILogger? _logger;

    public WhisperGreedyDecoder(ILogger? logger = null)
    {
        _logger = logger;
    }

    public WhisperWindowResult DecodeWindow(IWhisperModelRunner runner, Half[] features, int? languageToken)
    {
        runner.Encode(features);

        var length = runner.DecodeLength;
        var mask = new Half[length];
        Array.Fill(mask, (Half)MaskNeg);

        var sequence = new List<int> { WhisperTokens.Sot };
        if (languageToken is int lang)
        {
            sequence.AddRange([lang, WhisperTokens.Transcribe, WhisperTokens.NoTimestamps]);
        }

        var detected = languageToken;
        var text = new List<int>();
        var steps = 0;
        for (var n = 0; n < length - 1; n++)
        {
            mask[length - n - 1] = Half.Zero;
            var logits = runner.Decode(sequence[n], n, mask);
            steps++;

            if (n < sequence.Count - 1)
            {
                continue; // forced prefix token
            }

            if (detected is null)
            {
                detected = ArgMax(logits, WhisperTokens.FirstLanguage, WhisperTokens.LastLanguage + 1);
                _logger?.LogInformation(
                    "Whisper NPU detected language {Language} (token {Token})",
                    WhisperTokens.LanguageCode(detected.Value),
                    detected.Value);
                sequence.AddRange([detected.Value, WhisperTokens.Transcribe, WhisperTokens.NoTimestamps]);
                continue;
            }

            if (text.Count == 0)
            {
                Suppress(logits, WhisperTokens.Space);
            }

            // Only plain text tokens or EOT may follow the prefix (no timestamps / specials).
            var best = ArgMax(logits, 0, WhisperTokens.Eot + 1);
            if (best == WhisperTokens.Eot)
            {
                break;
            }

            text.Add(best);
            sequence.Add(best);
            if (TrimRepetition(text))
            {
                _logger?.LogWarning("Whisper NPU stopped a repetition loop after {Count} tokens", text.Count);
                break;
            }
        }

        return new WhisperWindowResult(text, WhisperTokens.LanguageCode(detected ?? -1), steps);
    }

    /// <summary>Detects a tail of ≥<see cref="MinRepeats"/> copies of a 1..8 token pattern and trims to one copy.</summary>
    internal static bool TrimRepetition(List<int> tokens)
    {
        for (var period = 1; period <= MaxRepeatPeriod; period++)
        {
            var needed = period * MinRepeats;
            if (tokens.Count < needed)
            {
                break;
            }

            var start = tokens.Count - needed;
            var repeating = true;
            for (var i = start + period; i < tokens.Count && repeating; i++)
            {
                repeating = tokens[i] == tokens[i - period];
            }

            if (repeating)
            {
                tokens.RemoveRange(start + period, needed - period);
                return true;
            }
        }

        return false;
    }

    private static void Suppress(float[] logits, int id)
    {
        if (id < logits.Length)
        {
            logits[id] = float.NegativeInfinity;
        }
    }

    internal static int ArgMax(float[] logits, int from, int to)
    {
        to = Math.Min(to, logits.Length);
        var best = from;
        var bestValue = float.NegativeInfinity;
        for (var i = from; i < to; i++)
        {
            if (logits[i] > bestValue)
            {
                bestValue = logits[i];
                best = i;
            }
        }

        return best;
    }
}
