namespace echo.Engines.ParakeetNpu.Whisper;

/// <summary>Special token ids of Whisper large-v3 / large-v3-turbo (vocab 51866).</summary>
public static class WhisperTokens
{
    public const int Eot = 50257;
    public const int Sot = 50258;
    public const int FirstLanguage = 50259;
    public const int Translate = 50359;
    public const int Transcribe = 50360;
    public const int NoTimestamps = 50364;
    public const int Space = 220;

    /// <summary>Whisper language codes in token order (large-v3: 100 languages, en = 50259).</summary>
    public static IReadOnlyList<string> Languages { get; } =
    [
        "en", "zh", "de", "es", "ru", "ko", "fr", "ja", "pt", "tr", "pl", "ca", "nl", "ar", "sv", "it", "id", "hi",
        "fi", "vi", "he", "uk", "el", "ms", "cs", "ro", "da", "hu", "ta", "no", "th", "ur", "hr", "bg", "lt", "la",
        "mi", "ml", "cy", "sk", "te", "fa", "lv", "bn", "sr", "az", "sl", "kn", "et", "mk", "br", "eu", "is", "hy",
        "ne", "mn", "bs", "kk", "sq", "sw", "gl", "mr", "pa", "si", "km", "sn", "yo", "so", "af", "oc", "ka", "be",
        "tg", "sd", "gu", "am", "yi", "lo", "uz", "fo", "ht", "ps", "tk", "nn", "mt", "sa", "lb", "my", "bo", "tl",
        "mg", "as", "tt", "haw", "ln", "ha", "ba", "jw", "su", "yue",
    ];

    public static int LastLanguage => FirstLanguage + Languages.Count - 1;

    /// <summary>Language token for a code like "ru"; null for "auto"/unknown (auto-detect).</summary>
    public static int? LanguageToken(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var normalized = code.Trim().ToLowerInvariant();
        var dash = normalized.IndexOfAny(['-', '_']);
        if (dash > 0)
        {
            normalized = normalized[..dash];
        }

        for (var i = 0; i < Languages.Count; i++)
        {
            if (Languages[i] == normalized)
            {
                return FirstLanguage + i;
            }
        }

        return null;
    }

    public static string? LanguageCode(int token) =>
        token >= FirstLanguage && token <= LastLanguage ? Languages[token - FirstLanguage] : null;
}
