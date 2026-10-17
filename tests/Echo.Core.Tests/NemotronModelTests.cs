using echo.Abstractions.Core;
using echo.Core;
using echo.Engines.Nemotron;

namespace echo.Core.Tests;

public sealed class NemotronModelTests
{
    [Fact]
    public void SpecForEngine_ReturnsOnDemandHuggingFaceSpec()
    {
        var spec = ModelRegistry.SpecForEngine("nemotron", "small", "e2e");
        Assert.NotNull(spec);
        Assert.Equal("nemotron", spec!.Engine);
        Assert.Equal(
            "csukuangfj2/sherpa-onnx-nemotron-3.5-asr-streaming-0.6b-560ms-int8-2026-06-11",
            spec.RepoId);
        Assert.Null(spec.GitHubReleaseTag);
        Assert.Equal(AppPaths.NemotronDir, spec.LocalDir);
        Assert.Equal(
            ["encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", "tokens.txt"],
            spec.AllowPatterns);
    }

    [Fact]
    public void IsNemotronDownloaded_RequiresAllFourFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "echo-nemotron-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var f in ModelRegistry.NemotronRequiredFiles.Take(3))
            {
                File.WriteAllText(Path.Combine(dir, f), "x");
            }

            Assert.False(ModelRegistry.IsNemotronDownloaded(dir));
            File.WriteAllText(Path.Combine(dir, "tokens.txt"), "x");
            Assert.True(ModelRegistry.IsNemotronDownloaded(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("ru", "ru")]
    [InlineData("EN", "en")]
    [InlineData("auto", "auto")]
    [InlineData("", "auto")]
    [InlineData(null, "auto")]
    public void NemotronLanguageFor_MapsSttLanguage(string? language, string expected)
    {
        Assert.Equal(expected, ModelRegistry.NemotronLanguageFor(language));
    }

    [Fact]
    public void BuildConfig_UsesCpuTransducerFilesFromModelDir()
    {
        var config = NemotronEngine.BuildConfig("/m", 16000, 4);
        Assert.Equal(Path.Combine("/m", "encoder.int8.onnx"), config.ModelConfig.Transducer.Encoder);
        Assert.Equal(Path.Combine("/m", "decoder.int8.onnx"), config.ModelConfig.Transducer.Decoder);
        Assert.Equal(Path.Combine("/m", "joiner.int8.onnx"), config.ModelConfig.Transducer.Joiner);
        Assert.Equal(Path.Combine("/m", "tokens.txt"), config.ModelConfig.Tokens);
        Assert.Equal("cpu", config.ModelConfig.Provider);
        Assert.Equal(4, config.ModelConfig.NumThreads);
    }

    [Theory]
    [InlineData("  a  b \n c ", "a b c")]
    [InlineData("", "")]
    public void CollapseWhitespace_NormalizesSegmentGaps(string input, string expected)
    {
        Assert.Equal(expected, NemotronEngine.CollapseWhitespace(input));
    }

    [Fact]
    public void AppConfig_ListsNemotronAndDevicePolicyIsCpuOnly()
    {
        Assert.Contains("nemotron", AppConfig.Engines);
        Assert.Equal("cpu", SettingsEngineDevicePolicy.NormalizeDeviceForEngine("nemotron", "npu"));
        Assert.Equal("cpu", SettingsEngineDevicePolicy.NormalizeDeviceForEngine("nemotron", "directml"));
    }

    [Fact]
    public async Task EnsureLoaded_WithoutModel_ThrowsFriendlyError()
    {
        if (ModelRegistry.IsNemotronDownloaded(AppPaths.NemotronDir))
        {
            return;
        }

        using var engine = new NemotronEngine(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NemotronEngine>.Instance);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.EnsureLoadedAsync());
        Assert.Contains("Nemotron", ex.Message);
    }
}

/// <summary>Opt-in: set ECHO_NEMOTRON_MODEL_DIR to a downloaded model dir with test_wavs/*.wav.</summary>
public sealed class NemotronEngineIntegrationTests
{
    [Theory]
    [InlineData("en", "en.wav", "auto")]
    [InlineData("uk", "uk.wav", "uk")]
    [InlineData("ru", "ru.wav", "ru")]
    [InlineData("ru-auto", "ru.wav", "auto")]
    public async Task Transcribe_RealModel_ProducesText(string label, string wav, string language)
    {
        var dir = Environment.GetEnvironmentVariable("ECHO_NEMOTRON_MODEL_DIR");
        var wavPath = dir is null ? null : Path.Combine(dir, "test_wavs", wav);
        if (dir is null || !File.Exists(wavPath))
        {
            return;
        }

        var (samples, rate) = ReadPcm16Wav(wavPath!);
        using var engine = new NemotronEngine(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NemotronEngine>.Instance, dir);
        engine.Configure(new echo.Abstractions.Engines.EngineOptions
        {
            Engine = "nemotron",
            Language = language,
            SampleRate = rate,
        });
        await engine.EnsureLoadedAsync();
        var text = await engine.TranscribeAsync(samples, rate);
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "echo-nemotron-it.txt"), $"{label}: {text}\n");
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    private static (float[] Samples, int Rate) ReadPcm16Wav(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var rate = BitConverter.ToInt32(bytes, 24);
        var channels = BitConverter.ToInt16(bytes, 22);
        var pos = 12;
        while (pos + 8 <= bytes.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
            var size = BitConverter.ToInt32(bytes, pos + 4);
            if (id == "data")
            {
                var frames = size / 2 / channels;
                var samples = new float[frames];
                for (var i = 0; i < frames; i++)
                {
                    samples[i] = BitConverter.ToInt16(bytes, pos + 8 + i * 2 * channels) / 32768f;
                }

                return (samples, rate);
            }

            pos += 8 + size;
        }

        throw new InvalidDataException("No data chunk");
    }
}
