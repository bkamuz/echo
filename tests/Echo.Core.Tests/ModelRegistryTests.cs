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
}
