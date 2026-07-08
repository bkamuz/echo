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
        var dir = CreateTempGigaAmDir();
        try
        {
            WriteE2eBundle(dir);
            File.WriteAllText(Path.Combine(dir, "gigaam_v3_rnnt_encoder.onnx"), "");

            var paths = GigaAmEngine.ResolveModelPaths(dir);
            Assert.NotNull(paths);
            Assert.Contains("gigaam_v3_e2e_rnnt_encoder.onnx", paths!.Encoder, StringComparison.Ordinal);
            Assert.Contains("gigaam_v3_e2e_rnnt_decoder.onnx", paths.Decoder, StringComparison.Ordinal);
            Assert.Contains("gigaam_v3_e2e_rnnt_joint.onnx", paths.Joiner, StringComparison.Ordinal);
            Assert.Contains("gigaam_v3_e2e_rnnt_tokens.txt", paths.Tokens, StringComparison.Ordinal);
            AssertAllPathsExist(paths);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("gigaam_v3_e2e_rnnt_decoder.onnx")]
    [InlineData("gigaam_v3_e2e_rnnt_joint.onnx")]
    [InlineData("gigaam_v3_e2e_rnnt_tokens.txt")]
    public void GigaAm_ResolveModelPaths_ReturnsNull_WhenE2eBundleIncomplete(string fileToOmit)
    {
        var dir = CreateTempGigaAmDir();
        try
        {
            if (string.IsNullOrEmpty(fileToOmit))
            {
                File.WriteAllText(Path.Combine(dir, "gigaam_v3_e2e_rnnt_encoder.onnx"), "");
            }
            else
            {
                WriteE2eBundle(dir);
                File.Delete(Path.Combine(dir, fileToOmit));
            }

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
        var dir = CreateTempGigaAmDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "gigaam_v3_e2e_rnnt_encoder.onnx"), "");
            WriteRnntBundle(dir);

            var paths = GigaAmEngine.ResolveModelPaths(dir);
            Assert.NotNull(paths);
            Assert.Contains("gigaam_v3_rnnt_encoder.onnx", paths!.Encoder, StringComparison.Ordinal);
            AssertAllPathsExist(paths);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void AssertAllPathsExist(GigaAmEngine.GigaAmModelPaths paths)
    {
        Assert.True(File.Exists(paths.Encoder));
        Assert.True(File.Exists(paths.Decoder));
        Assert.True(File.Exists(paths.Joiner));
        Assert.True(File.Exists(paths.Tokens));
    }

    private static string CreateTempGigaAmDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "echo-gigaam-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteE2eBundle(string dir)
    {
        File.WriteAllText(Path.Combine(dir, "gigaam_v3_e2e_rnnt_encoder.onnx"), "");
        File.WriteAllText(Path.Combine(dir, "gigaam_v3_e2e_rnnt_decoder.onnx"), "");
        File.WriteAllText(Path.Combine(dir, "gigaam_v3_e2e_rnnt_joint.onnx"), "");
        File.WriteAllText(Path.Combine(dir, "gigaam_v3_e2e_rnnt_tokens.txt"), "");
    }

    private static void WriteRnntBundle(string dir)
    {
        File.WriteAllText(Path.Combine(dir, "gigaam_v3_rnnt_encoder.onnx"), "");
        File.WriteAllText(Path.Combine(dir, "gigaam_v3_rnnt_decoder.onnx"), "");
        File.WriteAllText(Path.Combine(dir, "gigaam_v3_rnnt_joint.onnx"), "");
        File.WriteAllText(Path.Combine(dir, "gigaam_v3_rnnt_tokens.txt"), "");
    }

    [Fact]
    public void ModelRegistry_UsesSherpaCompatibleRepo()
    {
        var spec = ModelRegistry.GigaAmSpec();
        Assert.Contains("sherpa", spec.RepoId, StringComparison.OrdinalIgnoreCase);
    }
}
