using echo.Abstractions.Core;
using echo.Engines.Whisper;
using Whisper.net.Ggml;

namespace echo.Core.Tests;

public class WhisperModelTests
{
    [Fact]
    public void WhisperGgmlPath_UsesExpectedFileName()
    {
        var path = ModelRegistry.WhisperGgmlPath("small");
        Assert.EndsWith(Path.Combine("whisper", "small", "ggml-small.bin"), path);
    }

    [Fact]
    public void IsWhisperDownloaded_RequiresGgmlFile()
    {
        const string size = "small";
        var ggmlPath = ModelRegistry.WhisperGgmlPath(size);
        var dir = Path.GetDirectoryName(ggmlPath)!;
        Directory.CreateDirectory(dir);

        var legacyPath = Path.Combine(dir, "model.bin");
        var hadGgml = File.Exists(ggmlPath);
        var hadLegacy = File.Exists(legacyPath);
        byte[]? ggmlBackup = hadGgml ? File.ReadAllBytes(ggmlPath) : null;
        byte[]? legacyBackup = hadLegacy ? File.ReadAllBytes(legacyPath) : null;

        try
        {
            if (hadGgml)
            {
                File.Delete(ggmlPath);
            }

            File.WriteAllText(legacyPath, "");
            Assert.False(ModelRegistry.IsWhisperDownloaded(size));
            Assert.False(ModelRegistry.WhisperSpec(size).IsDownloaded());

            File.WriteAllText(ggmlPath, "");
            Assert.True(ModelRegistry.IsWhisperDownloaded(size));
            Assert.True(ModelRegistry.WhisperSpec(size).IsDownloaded());
        }
        finally
        {
            if (hadGgml && ggmlBackup is not null)
            {
                File.WriteAllBytes(ggmlPath, ggmlBackup);
            }
            else if (File.Exists(ggmlPath))
            {
                File.Delete(ggmlPath);
            }

            if (hadLegacy && legacyBackup is not null)
            {
                File.WriteAllBytes(legacyPath, legacyBackup);
            }
            else if (File.Exists(legacyPath))
            {
                File.Delete(legacyPath);
            }
        }
    }

    [Fact]
    public void ResolveGgmlModelPath_ThrowsWhenMissing()
    {
        const string size = "tiny";
        var ggmlPath = ModelRegistry.WhisperGgmlPath(size);
        var dir = Path.GetDirectoryName(ggmlPath)!;
        Directory.CreateDirectory(dir);

        var hadGgml = File.Exists(ggmlPath);
        byte[]? ggmlBackup = hadGgml ? File.ReadAllBytes(ggmlPath) : null;

        try
        {
            if (hadGgml)
            {
                File.Delete(ggmlPath);
            }

            var ex = Assert.Throws<InvalidOperationException>(() => WhisperGgmlHelper.ResolveGgmlModelPath(size));
            Assert.Contains("Скачайте", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (hadGgml && ggmlBackup is not null)
            {
                File.WriteAllBytes(ggmlPath, ggmlBackup);
            }
            else if (File.Exists(ggmlPath))
            {
                File.Delete(ggmlPath);
            }
        }
    }

    [Theory]
    [InlineData("tiny", GgmlType.Tiny)]
    [InlineData("base", GgmlType.Base)]
    [InlineData("small", GgmlType.Small)]
    [InlineData("medium", GgmlType.Medium)]
    [InlineData("large-v3", GgmlType.LargeV3)]
    [InlineData("large-v3-turbo", GgmlType.LargeV3Turbo)]
    public void MapSizeToGgmlType_MapsKnownSizes(string size, GgmlType expected)
    {
        Assert.Equal(expected, WhisperGgmlHelper.MapSizeToGgmlType(size));
    }

    [Fact]
    public void WhisperSizeFromSpecId_ExtractsSizeWithHyphens()
    {
        Assert.Equal("large-v3-turbo", ModelRegistry.WhisperSizeFromSpecId("whisper-large-v3-turbo"));
    }
}
