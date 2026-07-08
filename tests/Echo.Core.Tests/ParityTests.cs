using echo.Core;
using echo.Abstractions.Core;

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
        var paths = ModelRegistry.ResolveGigaAmBundle(AppPaths.GigaAmDir, "e2e")
            ?? ModelRegistry.ResolveGigaAmBundle(AppPaths.GigaAmDir, "rnnt");
        if (paths is null)
        {
            return;
        }

        Assert.True(
            paths.Encoder.Contains("e2e_rnnt_encoder", StringComparison.Ordinal)
            || paths.Encoder.Contains("rnnt_encoder", StringComparison.Ordinal));
        Assert.True(File.Exists(paths.Encoder));
        Assert.True(File.Exists(paths.Decoder));
        Assert.True(File.Exists(paths.Joiner));
        Assert.True(File.Exists(paths.Tokens));
    }

    [Fact]
    public void GigaAm_ResolveModelPaths_E2eVariant_PrefersE2e()
    {
        var dir = GigaAmTestFixtures.CreateTempDir();
        try
        {
            GigaAmTestFixtures.WriteBundle(dir, ModelRegistry.GigaAmE2ePrefix);
            File.WriteAllText(Path.Combine(dir, $"{ModelRegistry.GigaAmRnntPrefix}_encoder.onnx"), "");

            var paths = ModelRegistry.ResolveGigaAmBundle(dir, "e2e");
            Assert.NotNull(paths);
            Assert.Contains(ModelRegistry.GigaAmE2ePrefix, paths!.Encoder, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GigaAm_ResolveModelPaths_RnntVariant_DoesNotFallbackToE2e()
    {
        var dir = GigaAmTestFixtures.CreateTempDir();
        try
        {
            GigaAmTestFixtures.WriteBundle(dir, ModelRegistry.GigaAmE2ePrefix);

            Assert.Null(ModelRegistry.ResolveGigaAmBundle(dir, "rnnt"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GigaAm_ResolveModelPaths_ReturnsNull_WhenBundleIncomplete()
    {
        var dir = GigaAmTestFixtures.CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, $"{ModelRegistry.GigaAmE2ePrefix}_encoder.onnx"), "");
            Assert.Null(ModelRegistry.ResolveGigaAmBundle(dir, "e2e"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GigaAm_ResolveModelPaths_RnntVariant_UsesRnntOnly()
    {
        var dir = GigaAmTestFixtures.CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, $"{ModelRegistry.GigaAmE2ePrefix}_encoder.onnx"), "");
            GigaAmTestFixtures.WriteBundle(dir, ModelRegistry.GigaAmRnntPrefix);

            Assert.Null(ModelRegistry.ResolveGigaAmBundle(dir, "e2e"));
            var paths = ModelRegistry.ResolveGigaAmBundle(dir, "rnnt");
            Assert.NotNull(paths);
            Assert.Contains(ModelRegistry.GigaAmRnntPrefix, paths!.Encoder, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ModelRegistry_UsesSherpaCompatibleRepo()
    {
        var spec = ModelRegistry.GigaAmSpecFor("e2e");
        Assert.NotNull(spec);
        Assert.Contains("sherpa", spec!.RepoId, StringComparison.OrdinalIgnoreCase);
    }
}
