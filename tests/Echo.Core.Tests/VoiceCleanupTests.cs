using echo.Core;

namespace echo.Core.Tests;

public sealed class VoiceCleanupTests
{
    private sealed class DelayEnhancer : ISpeechEnhancer
    {
        private readonly Queue<float> _line = new();
        public int SampleRate => 48000;
        public int FrameSize => 320;
        public int DelaySamples => 704;
        public int Resets { get; private set; }

        public void Reset()
        {
            Resets++;
            _line.Clear();
            for (var i = 0; i < DelaySamples; i++) _line.Enqueue(0f);
        }

        public void ProcessFrame(float[] input, float[] output)
        {
            for (var i = 0; i < FrameSize; i++)
            {
                _line.Enqueue(input[i]);
                output[i] = _line.Dequeue();
            }
        }
    }

    private static float[] Sine(int n, double hz, int rate, float amp = 0.3f) =>
        Enumerable.Range(0, n).Select(i => (float)(amp * Math.Sin(2 * Math.PI * hz * i / rate))).ToArray();

    [Fact]
    public void Resampler_16k_48k_RoundTrip_PreservesSpeechBand()
    {
        var x = Sine(16000, 440, 16000);
        var up = Resampler.Resample(x, 16000, 48000);
        Assert.Equal(48000, up.Length);
        var back = Resampler.Resample(up, 48000, 16000);
        Assert.Equal(16000, back.Length);
        var err = 0.0;
        for (var i = 200; i < 15800; i++) err = Math.Max(err, Math.Abs(back[i] - x[i]));
        Assert.True(err < 0.01, $"max err {err}");
    }

    [Fact]
    public void Process_CompensatesDelay_KeepsLengthAndAlignment()
    {
        var x = Sine(8000, 300, 16000);
        var fake = new DelayEnhancer();
        var r = VoiceCleanup.Process(x, 16000, fake);
        Assert.True(r.Applied);
        Assert.Equal(x.Length, r.Samples.Length);
        var err = 0.0;
        for (var i = 200; i < 7800; i++) err = Math.Max(err, Math.Abs(r.Samples[i] - x[i]));
        Assert.True(err < 0.01, $"max err {err}");
        Assert.True(fake.Resets >= 1);
    }

    [Fact]
    public void Process_EmptyOrUnavailable_ReturnsOriginal()
    {
        var empty = VoiceCleanup.Process([], 16000, new DelayEnhancer());
        Assert.False(empty.Applied);
        Assert.Empty(empty.Samples);
    }

    [Fact]
    public void AppConfig_VoiceCleanupDefaultsOffAndClones()
    {
        var c = new AppConfig();
        Assert.False(c.VoiceCleanup);
        c.VoiceCleanup = true;
        Assert.True(c.Clone().VoiceCleanup);
    }

    /// <summary>Runs the real bundled native on Linux x64 (skips silently elsewhere).</summary>
    [Fact]
    public void RealFastEnhancer_ReducesNoise_WhenNativeAvailable()
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../native/fe"));
        var rid = Path.Combine(dir, "linux-x64");
        if (!OperatingSystem.IsLinux() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
            != System.Runtime.InteropServices.Architecture.X64 || !File.Exists(Path.Combine(rid, "libecho_fe.so")))
        {
            return;
        }

        var tmp = Directory.CreateTempSubdirectory("echo-fe-").FullName;
        File.Copy(Path.Combine(rid, "libecho_fe.so"), Path.Combine(tmp, "libecho_fe.so"));
        File.Copy(Path.Combine(dir, "fe.q8"), Path.Combine(tmp, "fe.q8"));
        FastEnhancerNative.BaseDir = tmp;
        var rng = new Random(1);
        var noise = Enumerable.Range(0, 32000).Select(_ => (float)(rng.NextDouble() * 2 - 1) * 0.05f).ToArray();
        var r = VoiceCleanup.Process(noise, 16000);
        Assert.True(r.Applied, r.Error);
        Assert.Equal(noise.Length, r.Samples.Length);
        double Rms(float[] a) => Math.Sqrt(a.Skip(8000).Average(v => (double)v * v));
        Assert.True(Rms(r.Samples) < Rms(noise) * 0.5, $"in {Rms(noise)} out {Rms(r.Samples)}");
        var warm = VoiceCleanup.Process(noise, 16000);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Resampler.Resample(Resampler.Resample(noise, 16000, 48000), 48000, 16000);
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "echo-fe-timing.txt"), $"first {r.ElapsedMs:F1} warm {warm.ElapsedMs:F1} resample-only {sw.Elapsed.TotalMilliseconds:F1} ms per 2000 ms\n");
    }
}

public sealed class VoiceCleanupDeliveryTests
{
    [Fact]
    public void EmbeddedPayload_ExtractsOnce_AndIsAvailableWithoutLooseFolder()
    {
        var root = Directory.CreateTempSubdirectory("echo-fe-x-").FullName;
        var oldBase = FastEnhancerNative.BaseDir;
        var oldRoot = FastEnhancerNative.ExtractRoot;
        var oldAsm = FastEnhancerNative.EmbeddedAssembly;
        try
        {
            FastEnhancerNative.BaseDir = Path.Combine(root, "missing");
            FastEnhancerNative.ExtractRoot = Path.Combine(root, "extract");
            FastEnhancerNative.EmbeddedAssembly = null;
            Assert.False(FastEnhancerNative.IsAvailable);

            FastEnhancerNative.EmbeddedAssembly = typeof(VoiceCleanupDeliveryTests).Assembly;
            Assert.True(FastEnhancerNative.IsAvailable);
            var dir = FastEnhancerNative.ResolveDirectory();
            Assert.StartsWith(FastEnhancerNative.ExtractRoot, dir);
            Assert.Equal(565108, new FileInfo(Path.Combine(dir, "fe.q8")).Length);
            Assert.True(File.Exists(Path.Combine(dir, FastEnhancerNative.NativeFileName)));
            Assert.Equal(dir, FastEnhancerNative.ResolveDirectory());
        }
        finally
        {
            FastEnhancerNative.BaseDir = oldBase;
            FastEnhancerNative.ExtractRoot = oldRoot;
            FastEnhancerNative.EmbeddedAssembly = oldAsm;
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void WindowsUpdaterScript_CopiesAllZipContents()
    {
        var m = typeof(echo.Platform.Windows.WindowsUpdateApplier).GetMethod(
            "BuildUpdaterScript",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var script = (string)m.Invoke(null, [1, @"C:\Echo", @"C:\stage"])!;
        Assert.Contains("Get-ChildItem -LiteralPath $stagingDir", script);
        Assert.Contains("-Recurse -Force", script);
    }
}
