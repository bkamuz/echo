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

        Assert.True(
            paths.Encoder.Contains("e2e_rnnt_encoder.onnx", StringComparison.Ordinal)
            || paths.Encoder.Contains("rnnt_encoder.onnx", StringComparison.Ordinal));
        Assert.True(File.Exists(paths.Encoder));
        Assert.True(File.Exists(paths.Decoder));
        Assert.True(File.Exists(paths.Joiner));
        Assert.True(File.Exists(paths.Tokens));
    }

    [Fact]
    public void GigaAm_ResolveModelPaths_PrefersE2e()
    {
        var dir = CreateTempDir();
        try
        {
            WriteBundle(dir, ModelRegistry.GigaAmE2ePrefix);
            File.WriteAllText(Path.Combine(dir, $"{ModelRegistry.GigaAmRnntPrefix}_encoder.onnx"), "");

            var paths = GigaAmEngine.ResolveModelPaths(dir);
            Assert.NotNull(paths);
            Assert.Contains(ModelRegistry.GigaAmE2ePrefix, paths!.Encoder, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GigaAm_ResolveModelPaths_ReturnsNull_WhenBundleIncomplete()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, $"{ModelRegistry.GigaAmE2ePrefix}_encoder.onnx"), "");
            Assert.Null(GigaAmEngine.ResolveModelPaths(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GigaAm_ResolveModelPaths_FallsThroughPartialE2e_ToCompleteRnnt()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, $"{ModelRegistry.GigaAmE2ePrefix}_encoder.onnx"), "");
            WriteBundle(dir, ModelRegistry.GigaAmRnntPrefix);

            var paths = GigaAmEngine.ResolveModelPaths(dir);
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
        var spec = ModelRegistry.GigaAmSpec();
        Assert.Contains("sherpa", spec.RepoId, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "echo-gigaam-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteBundle(string dir, string prefix)
    {
        File.WriteAllText(Path.Combine(dir, $"{prefix}_encoder.onnx"), "");
        File.WriteAllText(Path.Combine(dir, $"{prefix}_decoder.onnx"), "");
        File.WriteAllText(Path.Combine(dir, $"{prefix}_joint.onnx"), "");
        File.WriteAllText(Path.Combine(dir, $"{prefix}_tokens.txt"), "");
    }
}
