using echo.Abstractions.Core;
using echo.Core;

namespace echo.Core.Tests;

public class WhisperModelTests
{
    [Theory]
    [InlineData("tiny", "csukuangfj/sherpa-onnx-whisper-tiny", "tiny")]
    [InlineData("small", "csukuangfj/sherpa-onnx-whisper-small", "small")]
    [InlineData("large-v3", "csukuangfj/sherpa-onnx-whisper-large-v3", "large-v3")]
    [InlineData("large-v3-turbo", "csukuangfj/sherpa-onnx-whisper-turbo", "turbo")]
    public void WhisperSpec_PointsAtSherpaInt8Repo(string size, string repo, string prefix)
    {
        var spec = ModelRegistry.SpecForEngine("whisper", size, "e2e")!;
        Assert.Equal(repo, spec.RepoId);
        Assert.Equal(
            [$"{prefix}-encoder.int8.onnx", $"{prefix}-decoder.int8.onnx", $"{prefix}-tokens.txt"],
            spec.AllowPatterns);
        Assert.Equal(AppPaths.WhisperDir(size), spec.LocalDir);
    }

    [Fact]
    public void IsWhisperDownloaded_RequiresAllThreeFiles()
    {
        const string size = "tiny";
        var files = ModelRegistry.WhisperFiles(size);
        var dir = AppPaths.WhisperDir(size);
        var existed = Directory.Exists(dir);
        if (existed && ModelRegistry.IsWhisperDownloaded(size))
        {
            return; // never clobber a real download
        }

        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(files.Encoder, "x");
            File.WriteAllText(files.Decoder, "x");
            Assert.False(ModelRegistry.IsWhisperDownloaded(size));
            File.WriteAllText(files.Tokens, "x");
            Assert.True(ModelRegistry.IsWhisperDownloaded(size));
        }
        finally
        {
            foreach (var f in new[] { files.Encoder, files.Decoder, files.Tokens })
            {
                File.Delete(f);
            }

            if (!existed)
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("ru", "ru")]
    [InlineData("auto", "")]
    [InlineData(null, "")]
    public void WhisperLanguageFor(string? lang, string expected) =>
        Assert.Equal(expected, ModelRegistry.WhisperLanguageFor(lang));

    [Fact]
    public void AllConfiguredSizesResolve()
    {
        foreach (var size in AppConfig.WhisperSizes)
        {
            Assert.StartsWith("csukuangfj/sherpa-onnx-whisper-", ModelRegistry.WhisperRepo(size));
        }
    }
}
