using System.Text;
using System.Text.Json;

namespace echo.Engines.ParakeetNpu.Whisper;

/// <summary>
/// Decode-only byte-level BPE (GPT-2 style) for Whisper: id → token string → bytes → UTF-8.
/// Special/added tokens (id ≥ 50257: SOT, languages, tasks, timestamps) are skipped.
/// </summary>
public sealed class WhisperTokenizer
{
    private static readonly Dictionary<char, byte> UnicodeToByte = BuildUnicodeToByte();
    private readonly string?[] _idToToken;

    public WhisperTokenizer(IReadOnlyDictionary<string, int> vocab)
    {
        var size = vocab.Count == 0 ? 0 : vocab.Values.Max() + 1;
        _idToToken = new string?[size];
        foreach (var (token, id) in vocab)
        {
            if (id >= 0)
            {
                _idToToken[id] = token;
            }
        }
    }

    public int VocabSize => _idToToken.Length;

    public static WhisperTokenizer Load(string vocabJsonPath)
    {
        using var stream = File.OpenRead(vocabJsonPath);
        var vocab = JsonSerializer.Deserialize<Dictionary<string, int>>(stream)
            ?? throw new InvalidOperationException($"Whisper vocab is empty: {vocabJsonPath}");
        return new WhisperTokenizer(vocab);
    }

    public string Decode(IEnumerable<int> ids)
    {
        var bytes = new List<byte>();
        foreach (var id in ids)
        {
            if (id < 0 || id >= WhisperTokens.Eot || id >= _idToToken.Length)
            {
                continue;
            }

            var token = _idToToken[id];
            if (token is null)
            {
                continue;
            }

            foreach (var ch in token)
            {
                if (UnicodeToByte.TryGetValue(ch, out var b))
                {
                    bytes.Add(b);
                }
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static Dictionary<char, byte> BuildUnicodeToByte()
    {
        // GPT-2 bytes_to_unicode.
        var bs = new List<int>();
        for (var b = '!'; b <= '~'; b++)
        {
            bs.Add(b);
        }

        for (var b = 0xA1; b <= 0xAC; b++)
        {
            bs.Add(b);
        }

        for (var b = 0xAE; b <= 0xFF; b++)
        {
            bs.Add(b);
        }

        var cs = new List<int>(bs);
        var extra = 0;
        for (var b = 0; b < 256; b++)
        {
            if (!bs.Contains(b))
            {
                bs.Add(b);
                cs.Add(256 + extra);
                extra++;
            }
        }

        var map = new Dictionary<char, byte>(256);
        for (var i = 0; i < bs.Count; i++)
        {
            map[(char)cs[i]] = (byte)bs[i];
        }

        return map;
    }
}
