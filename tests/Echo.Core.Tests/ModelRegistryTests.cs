using echo.Abstractions.Core;

namespace echo.Core.Tests;

public class ModelRegistryTests
{
    [Fact]
    public void WhisperSizes_ContainsExpectedSizes()
    {
        Assert.Contains("tiny", ModelRegistry.WhisperSizes);
        Assert.Contains("small", ModelRegistry.WhisperSizes);
        Assert.Contains("large-v3-turbo", ModelRegistry.WhisperSizes);
    }

    [Fact]
    public void SpecForEngine_ReturnsGigaAmSpec()
    {
        var spec = ModelRegistry.SpecForEngine("gigaam", "small", "e2e");
        Assert.NotNull(spec);
        Assert.Equal("gigaam", spec!.Engine);
        Assert.Equal("gigaam-v3-e2e", spec.Id);
    }

    [Fact]
    public void SpecForEngine_ReturnsGigaAmE2eCtcSpec()
    {
        var spec = ModelRegistry.SpecForEngine("gigaam", "small", "e2e-ctc");
        Assert.NotNull(spec);
        Assert.Equal("gigaam-v3-e2e-ctc", spec!.Id);
        Assert.Equal(ModelRegistry.GigaAmE2eCtcAllowPatterns, spec.AllowPatterns);
    }

    [Fact]
    public void SpecForEngine_ReturnsNullForUnknownEngine()
    {
        Assert.Null(ModelRegistry.SpecForEngine("invalid", "small", "e2e"));
    }
}
