namespace echo.Abstractions.Core;

/// <summary>
/// Sherpa token readers treat CR as part of the token on some platforms.
/// Published Windows exports may ship CRLF — normalize to LF before load.
/// </summary>
public static class TokenFileNormalizer
{
    public static void EnsureUnixNewlines(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0 || !bytes.AsSpan().Contains((byte)'\r'))
        {
            return;
        }

        var text = System.Text.Encoding.UTF8.GetString(bytes)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        File.WriteAllText(path, text, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
