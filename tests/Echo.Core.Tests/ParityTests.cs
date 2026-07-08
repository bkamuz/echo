using echo.Core;
using echo.Abstractions.Core;
using echo.Engines.GigaAm;

namespace echo.Core.Tests;

public class ParityTests
{
    [Fact]
    public void Config_IsCompatibleWithOriginalPythonFormat()
    {
        var referencePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "backup", "reference", "config.json"));

        if (!File.Exists(referencePath))
        {
            return;
        }

        var json = File.ReadAllText(referencePath);
        var config = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json);
        Assert.NotNull(config);
        Assert.Equal("gigaam", config.Engine);
        Assert.Equal(16000, config.SampleRate);
        Assert.Equal(300, config.MinPressMs);
    }

    [Fact]
    public void GigaAm_ModelPaths_RequireSherpaCompatibleLayout()
    {
        var paths = GigaAmEngine.ResolveModelPaths(AppPaths.GigaAmDir);
        if (paths is null)
        {
            return;
        }

        Assert.Contains("gigaam_v3_rnnt_encoder.onnx", paths.Encoder, StringComparison.Ordinal);
        Assert.True(File.Exists(paths.Encoder));
        Assert.True(File.Exists(paths.Decoder));
        Assert.True(File.Exists(paths.Joiner));
        Assert.True(File.Exists(paths.Tokens));
    }

    [Fact]
    public void ModelRegistry_UsesSherpaCompatibleRepo()
    {
        var spec = ModelRegistry.GigaAmSpec();
        Assert.Contains("sherpa", spec.RepoId, StringComparison.OrdinalIgnoreCase);
    }
}
