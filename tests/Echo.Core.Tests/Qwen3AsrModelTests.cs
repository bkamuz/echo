using System.Diagnostics;
using echo.Abstractions.Core;
using echo.Core;
using echo.Engines.Qwen3Asr;
using Microsoft.Extensions.Logging.Abstractions;

namespace echo.Core.Tests;

public sealed class Qwen3AsrModelTests
{
    [Fact]
    public void SpecForEngine_ReturnsOnDemandHuggingFaceSpec()
    {
        var spec = ModelRegistry.SpecForEngine("qwen3_asr", "small", "e2e");
        Assert.NotNull(spec);
        Assert.Equal("qwen3_asr", spec!.Engine);
        Assert.Equal("csukuangfj2/sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25", spec.RepoId);
        Assert.Null(spec.GitHubReleaseTag);
        Assert.Equal(AppPaths.Qwen3AsrDir, spec.LocalDir);
        Assert.Contains("tokenizer/vocab.json", spec.AllowPatterns!);
        Assert.Equal(6, spec.AllowPatterns!.Count);
    }

    [Fact]
    public void IsQwen3AsrDownloaded_RequiresTokenizerFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "echo-qwen3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "tokenizer"));
        try
        {
            foreach (var f in ModelRegistry.Qwen3AsrRequiredFiles.Where(f => !f.EndsWith("vocab.json")))
            {
                File.WriteAllText(Path.Combine(dir, f), "x");
            }

            Assert.False(ModelRegistry.IsQwen3AsrDownloaded(dir));
            File.WriteAllText(Path.Combine(dir, "tokenizer", "vocab.json"), "x");
            Assert.True(ModelRegistry.IsQwen3AsrDownloaded(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("ru", "Russian")]
    [InlineData("EN", "English")]
    [InlineData("auto", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Qwen3AsrLanguageFor_MapsSttLanguage(string? language, string expected)
    {
        Assert.Equal(expected, ModelRegistry.Qwen3AsrLanguageFor(language));
    }

    [Fact]
    public void BuildConfig_UsesCpuAndRaisedTokenLimits()
    {
        var config = Qwen3AsrEngine.BuildConfig("/m", 16000, 4);
        var q = config.ModelConfig.Qwen3Asr;
        Assert.Equal(Path.Combine("/m", "conv_frontend.onnx"), q.ConvFrontend);
        Assert.Equal(Path.Combine("/m", "encoder.int8.onnx"), q.Encoder);
        Assert.Equal(Path.Combine("/m", "decoder.int8.onnx"), q.Decoder);
        Assert.Equal(Path.Combine("/m", "tokenizer"), q.Tokenizer);
        Assert.True(q.MaxNewTokens > 128);
        Assert.Equal("cpu", config.ModelConfig.Provider);
    }

    [Fact]
    public void IsSilent_DetectsEmptyAndQuietClips()
    {
        Assert.True(Qwen3AsrEngine.IsSilent([]));
        Assert.True(Qwen3AsrEngine.IsSilent(new float[16000]));
        Assert.True(Qwen3AsrEngine.IsSilent(Enumerable.Repeat(0.002f, 16000).ToArray()));
        Assert.False(Qwen3AsrEngine.IsSilent([0f, 0.2f, -0.3f]));
    }

    [Fact]
    public void DevicePolicyIsCpuOnly()
    {
        Assert.Contains("qwen3_asr", AppConfig.Engines);
        Assert.Equal("cpu", SettingsEngineDevicePolicy.NormalizeDeviceForEngine("qwen3_asr", "npu"));
        Assert.Equal("cpu", SettingsEngineDevicePolicy.NormalizeDeviceForEngine("qwen3_asr", "directml"));
    }

    [Fact]
    public async Task EnsureLoaded_WithoutModel_ThrowsFriendlyError()
    {
        var dir = Path.Combine(Path.GetTempPath(), "echo-qwen3-missing-" + Guid.NewGuid().ToString("N"));
        using var engine = new Qwen3AsrEngine(NullLogger<Qwen3AsrEngine>.Instance, dir);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.EnsureLoadedAsync());
        Assert.Contains("Qwen3-ASR", ex.Message);
    }
}

/// <summary>Opt-in: set ECHO_QWEN3_MODEL_DIR to a downloaded model dir with test_wavs/*.wav.</summary>
public sealed class Qwen3AsrEngineIntegrationTests
{
    private static string? Dir => Environment.GetEnvironmentVariable("ECHO_QWEN3_MODEL_DIR");

    [Theory]
    [InlineData("ru", "ru1.wav", "ru")]
    [InlineData("ru-auto", "ru1.wav", "auto")]
    [InlineData("en", "en.wav", "en")]
    [InlineData("codeswitch", "codeswitch.wav", "auto")]
    [InlineData("ru-long", "ru-long.wav", "ru")]
    [InlineData("noise", "noise2.wav", "auto")]
    public async Task Transcribe_RealModel_ProducesText(string label, string wav, string language)
    {
        var wavPath = Dir is null ? null : Path.Combine(Dir, "test_wavs", wav);
        if (wavPath is null || !File.Exists(wavPath))
        {
            return;
        }

        var (samples, rate) = WavReader.ReadPcm16(wavPath);
        using var engine = Create(language, rate);
        var sw = Stopwatch.StartNew();
        await engine.EnsureLoadedAsync();
        var load = sw.Elapsed.TotalSeconds;
        sw.Restart();
        var text = await engine.TranscribeAsync(samples, rate);
        var secs = sw.Elapsed.TotalSeconds;
        var audio = samples.Length / (double)rate;
        Log($"{label}: load={load:F1}s audio={audio:F1}s decode={secs:F2}s RTF={secs / audio:F2} :: {text}");
        if (label != "noise")
        {
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task Transcribe_Silence_ReturnsEmpty(int seconds)
    {
        if (Dir is null)
        {
            return;
        }

        using var engine = Create("ru", 16000);
        await engine.EnsureLoadedAsync();
        var rng = new Random(1);
        var samples = Enumerable.Range(0, seconds * 16000).Select(_ => (float)(rng.NextDouble() - 0.5) * 0.004f).ToArray();
        var text = await engine.TranscribeAsync(samples, 16000);
        Log($"silence{seconds}s: '{text}'");
        Assert.Equal(string.Empty, text);
    }

    private static Qwen3AsrEngine Create(string language, int rate)
    {
        var engine = new Qwen3AsrEngine(NullLogger<Qwen3AsrEngine>.Instance, Dir);
        engine.Configure(new echo.Abstractions.Engines.EngineOptions
        {
            Engine = "qwen3_asr",
            Language = language,
            SampleRate = rate,
        });
        return engine;
    }

    private static void Log(string line) =>
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "echo-qwen3-it.txt"), line + "\n");
}

internal static class WavReader
{
    public static (float[] Samples, int Rate) ReadPcm16(string path)
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
                size = Math.Min(size, bytes.Length - pos - 8);
                var frames = size / 2 / channels;
                var samples = new float[frames];
                for (var i = 0; i < frames; i++)
                {
                    samples[i] = BitConverter.ToInt16(bytes, pos + 8 + i * 2 * channels) / 32768f;
                }

                return (samples, rate);
            }

            pos += 8 + size + (size & 1);
        }

        throw new InvalidDataException("No data chunk");
    }
}
