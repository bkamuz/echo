using echo.Abstractions.Core;

namespace echo.Core.Tests;

public class ParakeetNpuModelTests
{
    [Fact]
    public void ModelRegistry_ParakeetNpuSpec_UsesExpectedEngineAndDir()
    {
        var spec = ModelRegistry.ParakeetNpuSpec();
        Assert.Equal("parakeet_npu", spec.Engine);
        Assert.Equal(AppPaths.ParakeetNpuDir, spec.LocalDir);
        Assert.Equal("parakeet-npu-htp", spec.Id);
    }

    [Fact]
    public void SpecForEngine_ResolvesParakeetNpu()
    {
        var spec = ModelRegistry.SpecForEngine("parakeet_npu", "small", "e2e");
        Assert.NotNull(spec);
        Assert.Equal("parakeet_npu", spec!.Engine);
    }
}
