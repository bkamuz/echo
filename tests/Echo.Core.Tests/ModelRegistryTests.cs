using echo.Abstractions.Core;

namespace echo.Core.Tests;

public class ModelRegistryTests
{
    [Fact]
    public void AllModels_ReturnsGigaAmAndWhisperSizes()
    {
        var models = ModelRegistry.AllModels();
        Assert.Contains(models, m => m.Engine == "gigaam");
        Assert.Contains(models, m => m.Engine == "whisper");
        Assert.Equal(1 + ModelRegistry.WhisperSizes.Count, models.Count);
    }

    [Fact]
    public void GetModel_ReturnsNullForUnknownId()
    {
        Assert.Null(ModelRegistry.GetModel("nonexistent"));
    }

    [Fact]
    public void GetModel_ReturnsSpecForKnownId()
    {
        var spec = ModelRegistry.GetModel("gigaam-v3");
        Assert.NotNull(spec);
        Assert.Equal("gigaam", spec!.Engine);
    }

    [Fact]
    public void WhisperSizes_ContainsExpectedSizes()
    {
        Assert.Contains("tiny", ModelRegistry.WhisperSizes);
        Assert.Contains("small", ModelRegistry.WhisperSizes);
        Assert.Contains("large-v3-turbo", ModelRegistry.WhisperSizes);
    }

    [Fact]
    public void GigaAm_IsDownloaded_RequiresFullBundle()
    {
        var dir = Path.Combine(Path.GetTempPath(), "echo-gigaam-spec-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var spec = new ModelSpec(
                Id: "gigaam-v3-test",
                Title: "GigaAM v3 test",
                Engine: "gigaam",
                RepoId: ModelRegistry.GigaAmRepo,
                LocalDir: dir);

            File.WriteAllText(Path.Combine(dir, $"{ModelRegistry.GigaAmE2ePrefix}_encoder.onnx"), "");
            Assert.False(spec.IsDownloaded());

            File.WriteAllText(Path.Combine(dir, $"{ModelRegistry.GigaAmE2ePrefix}_decoder.onnx"), "");
            File.WriteAllText(Path.Combine(dir, $"{ModelRegistry.GigaAmE2ePrefix}_joint.onnx"), "");
            File.WriteAllText(Path.Combine(dir, $"{ModelRegistry.GigaAmE2ePrefix}_tokens.txt"), "");
            Assert.True(spec.IsDownloaded());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
