using echo.Abstractions.Core;

namespace echo.Core.Tests;

public class ModelRegistryTests
{
    [Fact]
    public void AllModels_ReturnsAllEngines()
    {
        var models = ModelRegistry.AllModels();
        Assert.Contains(models, m => m.Engine == "gigaam");
        Assert.Contains(models, m => m.Engine == "whisper");
        Assert.Contains(models, m => m.Engine == "omnilingual");
        Assert.Equal(3 + ModelRegistry.WhisperSizes.Count, models.Count);
    }

    [Fact]
    public void GetModel_ReturnsNullForUnknownId()
    {
        Assert.Null(ModelRegistry.GetModel("nonexistent"));
    }

    [Fact]
    public void GetModel_ReturnsSpecForKnownId()
    {
        var spec = ModelRegistry.GetModel("gigaam-v3-e2e");
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
    public void GigaAm_IsDownloaded_RequiresFullBundleForVariant()
    {
        var dir = GigaAmTestFixtures.CreateTempDir();
        try
        {
            var e2eSpec = new ModelSpec(
                Id: "gigaam-v3-e2e",
                Title: "GigaAM v3 e2e test",
                Engine: "gigaam",
                RepoId: ModelRegistry.GigaAmRepo,
                LocalDir: dir);
            var rnntSpec = new ModelSpec(
                Id: "gigaam-v3-rnnt",
                Title: "GigaAM v3 rnnt test",
                Engine: "gigaam",
                RepoId: ModelRegistry.GigaAmRepo,
                LocalDir: dir);

            File.WriteAllText(Path.Combine(dir, $"{ModelRegistry.GigaAmE2ePrefix}_encoder.onnx"), "");
            Assert.False(e2eSpec.IsDownloaded());
            Assert.False(rnntSpec.IsDownloaded());

            File.WriteAllText(Path.Combine(dir, $"{ModelRegistry.GigaAmE2ePrefix}_decoder.onnx"), "");
            File.WriteAllText(Path.Combine(dir, $"{ModelRegistry.GigaAmE2ePrefix}_joint.onnx"), "");
            File.WriteAllText(Path.Combine(dir, $"{ModelRegistry.GigaAmE2ePrefix}_tokens.txt"), "");
            Assert.True(e2eSpec.IsDownloaded());
            Assert.False(rnntSpec.IsDownloaded());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
