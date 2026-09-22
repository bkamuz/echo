using System.Security.Cryptography;

namespace echo.Engines.ParakeetNpu;

internal static class AssetVerifier
{
    public static void VerifyFile(string path, long expectedBytes, string expectedSha256)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Missing asset: {path}");
        }

        var info = new FileInfo(path);
        if (info.Length != expectedBytes)
        {
            throw new InvalidOperationException(
                $"Size mismatch for {Path.GetFileName(path)}: expected {expectedBytes}, got {info.Length}.");
        }

        var actual = ComputeSha256Hex(path);
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SHA-256 mismatch for {Path.GetFileName(path)}: expected {expectedSha256}, got {actual}.");
        }
    }

    public static string ComputeSha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task VerifyWheelSha256Async(Stream wheelStream, string expectedSha256, CancellationToken ct)
    {
        wheelStream.Position = 0;
        var hash = await SHA256.HashDataAsync(wheelStream, ct).ConfigureAwait(false);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Wheel SHA-256 mismatch: expected {expectedSha256}, got {actual}.");
        }

        wheelStream.Position = 0;
    }
}
