using echo.Core;
using echo.Abstractions.Core;

namespace echo.Core.Tests;

public class AppConfigTests
{
    [Fact]
    public void Normalize_FixesInvalidEngine()
    {
        var config = new AppConfig { Engine = "invalid" };
        config.Normalize();
        Assert.Equal("gigaam", config.Engine);
    }

    [Fact]
    public void ModelRegistry_GigaAmSpec_PointsToAppData()
    {
        var spec = ModelRegistry.GigaAmSpec();
        Assert.Equal("Smirnov75/GigaAM-v3-sherpa-onnx", spec.RepoId);
        Assert.Contains("gigaam-v3", spec.LocalDir);
    }
}
