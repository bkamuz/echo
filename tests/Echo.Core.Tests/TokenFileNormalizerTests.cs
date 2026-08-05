using echo.Abstractions.Core;

namespace echo.Core.Tests;

public sealed class TokenFileNormalizerTests
{
    [Fact]
    public void EnsureUnixNewlines_RewritesCrlfInPlace()
    {
        var path = Path.Combine(Path.GetTempPath(), $"echo-tokens-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllBytes(path, "a 0\r\nb 1\r\n<blk> 2\r\n"u8.ToArray());

            TokenFileNormalizer.EnsureUnixNewlines(path);

            var raw = File.ReadAllBytes(path);
            Assert.DoesNotContain((byte)'\r', raw);
            Assert.Equal("a 0\nb 1\n<blk> 2\n", System.Text.Encoding.UTF8.GetString(raw));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EnsureUnixNewlines_NoOpWhenAlreadyLf()
    {
        var path = Path.Combine(Path.GetTempPath(), $"echo-tokens-{Guid.NewGuid():N}.txt");
        try
        {
            var original = "a 0\nb 1\n<blk> 2\n"u8.ToArray();
            File.WriteAllBytes(path, original);
            var before = File.GetLastWriteTimeUtc(path);

            TokenFileNormalizer.EnsureUnixNewlines(path);

            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal(before, File.GetLastWriteTimeUtc(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
