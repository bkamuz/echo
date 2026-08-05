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
    public void Clone_CopiesMutableFieldsWithoutSharingExtra()
    {
        var original = new AppConfig
        {
            Hotkey = "ctrl+shift+x",
            Engine = "omnilingual",
            Device = "directml",
        };
        original.Extra["k"] = System.Text.Json.JsonSerializer.SerializeToElement(1);

        var clone = original.Clone();
        clone.Hotkey = "ctrl+cmd";
        clone.Extra["k"] = System.Text.Json.JsonSerializer.SerializeToElement(2);

        Assert.Equal("ctrl+shift+x", original.Hotkey);
        Assert.Equal(1, original.Extra["k"].GetInt32());
        Assert.Equal("ctrl+cmd", clone.Hotkey);
        Assert.Equal("omnilingual", clone.Engine);
        Assert.Equal("directml", clone.Device);
        Assert.Equal(original.UiLanguage, clone.UiLanguage);
        Assert.Equal(original.PostReleaseMs, clone.PostReleaseMs);
    }

    [Fact]
    public void ModelRegistry_GigaAmSpec_PointsToAppData()
    {
        var spec = ModelRegistry.GigaAmSpecFor("e2e");
        Assert.NotNull(spec);
        Assert.Equal("Smirnov75/GigaAM-v3-sherpa-onnx", spec!.RepoId);
        Assert.Contains("gigaam-v3", spec.LocalDir);
    }

    [Fact]
    public void StartWithSystem_DefaultsToFalse()
    {
        var config = new AppConfig();
        Assert.False(config.StartWithSystem);
    }

    [Fact]
    public void UiLanguage_DefaultsToSystem()
    {
        var config = new AppConfig();
        Assert.Equal("system", config.UiLanguage);
    }

    [Theory]
    [InlineData("de", "system")]
    [InlineData("EN", "en")]
    [InlineData("ru", "ru")]
    public void Normalize_UiLanguage(string input, string expected)
    {
        var config = new AppConfig { UiLanguage = input };
        config.Normalize();
        Assert.Equal(expected, config.UiLanguage);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(500, 500)]
    [InlineData(2000, 1500)]
    public void Normalize_ClampsPostReleaseMs(int input, int expected)
    {
        var config = new AppConfig { PostReleaseMs = input };
        config.Normalize();
        Assert.Equal(expected, config.PostReleaseMs);
    }

    [Fact]
    public void PostReleaseMs_DefaultsTo500()
    {
        var config = new AppConfig();
        Assert.Equal(500, config.PostReleaseMs);
    }

    [Theory]
    [InlineData("v3", "e2e")]
    [InlineData("v3-punct", "e2e")]
    public void Normalize_MigratesLegacyGigaAmModelSize(string legacy, string expected)
    {
        var config = new AppConfig { GigaAmModelSize = legacy };
        config.Normalize();
        Assert.Equal(expected, config.GigaAmModelSize);
    }
}
