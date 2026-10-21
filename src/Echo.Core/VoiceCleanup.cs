using System.Diagnostics;
using System.Runtime.InteropServices;

namespace echo.Core;

/// <summary>Streaming 48 kHz mono speech enhancer (one frame in, one frame out).</summary>
public interface ISpeechEnhancer
{
    int SampleRate { get; }
    int FrameSize { get; }
    /// <summary>Fixed algorithmic delay of the output, in model-rate samples.</summary>
    int DelaySamples { get; }
    void Reset();
    void ProcessFrame(float[] input, float[] output);
}

/// <summary>
/// Optional "voice cleanup": runs FastEnhancer-Medium (faster-enhancer.c, MIT, 565 KB int8 weights)
/// on captured audio before transcription. 16 kHz → 48 kHz → enhance → 16 kHz, delay-compensated.
/// </summary>
public static class VoiceCleanup
{
    public sealed record Result(float[] Samples, double ElapsedMs, bool Applied, string? Error);

    public static Result Process(float[] samples, int sampleRate, ISpeechEnhancer? enhancer = null)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            string? err = null;
            enhancer ??= FastEnhancerNative.TryGet(out err);
            if (enhancer is null)
            {
                return new Result(samples, 0, false, err);
            }

            if (samples.Length == 0)
            {
                return new Result(samples, 0, false, null);
            }

            var modelRate = enhancer.SampleRate;
            var up = Resampler.Resample(samples, sampleRate, modelRate);
            var frame = enhancer.FrameSize;
            var delay = enhancer.DelaySamples;
            var total = up.Length + delay;
            total = (total + frame - 1) / frame * frame;
            var outBuf = new float[total];
            var inF = new float[frame];
            var outF = new float[frame];
            lock (enhancer)
            {
                enhancer.Reset();
                for (var pos = 0; pos < total; pos += frame)
                {
                    Array.Clear(inF);
                    var n = Math.Clamp(up.Length - pos, 0, frame);
                    if (n > 0)
                    {
                        Array.Copy(up, pos, inF, 0, n);
                    }

                    enhancer.ProcessFrame(inF, outF);
                    Array.Copy(outF, 0, outBuf, pos, frame);
                }

                enhancer.Reset();
            }

            var aligned = new float[up.Length];
            Array.Copy(outBuf, delay, aligned, 0, up.Length);
            for (var i = 0; i < aligned.Length; i++)
            {
                if (!float.IsFinite(aligned[i]))
                {
                    return new Result(samples, sw.Elapsed.TotalMilliseconds, false, "non-finite output");
                }
            }

            var down = Resampler.Resample(aligned, modelRate, sampleRate);
            if (down.Length != samples.Length)
            {
                Array.Resize(ref down, samples.Length);
            }

            return new Result(down, sw.Elapsed.TotalMilliseconds, true, null);
        }
        catch (Exception ex)
        {
            return new Result(samples, sw.Elapsed.TotalMilliseconds, false, ex.Message);
        }
    }
}

/// <summary>Windowed-sinc rational resampler (exact for integer ratios such as 16k↔48k).</summary>
public static class Resampler
{
    public static float[] Resample(float[] x, int fromRate, int toRate)
    {
        if (fromRate == toRate || x.Length == 0)
        {
            return (float[])x.Clone();
        }

        var g = Gcd(fromRate, toRate);
        var l = toRate / g;   // upsample
        var m = fromRate / g; // downsample
        var outLen = (int)((long)x.Length * l / m);
        // Low-pass at the lower Nyquist, expressed relative to the upsampled rate.
        var cutoff = 0.5 / Math.Max(l, m) * 0.92;
        const int halfTaps = 16; // per input-rate zero crossing span
        var half = halfTaps * Math.Max(l, m);
        var h = new double[2 * half + 1];
        for (var i = -half; i <= half; i++)
        {
            var t = i;
            var sinc = t == 0 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * t) / (Math.PI * t);
            var w = 0.42 + 0.5 * Math.Cos(Math.PI * i / half) + 0.08 * Math.Cos(2 * Math.PI * i / half); // Blackman
            h[i + half] = sinc * w * l;
        }

        var y = new float[outLen];
        for (var n = 0; n < outLen; n++)
        {
            // Position on the upsampled grid.
            long u = (long)n * m;
            // Contributions from x[k] at grid position k*l, where |u - k*l| <= half.
            var kMin = (long)Math.Ceiling((u - half) / (double)l);
            var kMax = (long)Math.Floor((u + half) / (double)l);
            double acc = 0;
            for (var k = Math.Max(0, kMin); k <= Math.Min(x.Length - 1, kMax); k++)
            {
                acc += x[k] * h[u - k * l + half];
            }

            y[n] = (float)acc;
        }

        return y;
    }

    private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
}

/// <summary>P/Invoke wrapper over bundled echo_fe (faster-enhancer.c). Global engine; serialized by lock.</summary>
public sealed class FastEnhancerNative : ISpeechEnhancer
{
    private static readonly object Gate = new();
    private static FastEnhancerNative? _instance;
    private static string? _loadError;

    private readonly IntPtr _weights;
    private readonly Run _run;
    private readonly Action _reset;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Init(IntPtr w, int n);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Run(float[] input, float[] output);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VoidFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int IntFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int TierFn([MarshalAs(UnmanagedType.LPStr)] string tier);

    private FastEnhancerNative(IntPtr weights, Run run, Action reset, int frame, int rate)
    {
        _weights = weights;
        _run = run;
        _reset = reset;
        FrameSize = frame;
        SampleRate = rate;
    }

    public int SampleRate { get; }
    public int FrameSize { get; }
    public int DelaySamples => 704;

    public void Reset() => _reset();

    public void ProcessFrame(float[] input, float[] output) => _run(input, output);

    public static string NativeFileName =>
        OperatingSystem.IsWindows() ? "echo_fe.dll" : OperatingSystem.IsMacOS() ? "libecho_fe.dylib" : "libecho_fe.so";

    /// <summary>Directory holding echo_fe + fe.q8 (next to the app; overridable for tests).</summary>
    public static string BaseDir { get; set; } = Path.Combine(AppContext.BaseDirectory, "fe");

    /// <summary>Assembly carrying embedded "echo.fe.native" + "echo.fe.q8" (the app sets this at startup).</summary>
    public static System.Reflection.Assembly? EmbeddedAssembly { get; set; }

    /// <summary>Where embedded payloads are extracted (default %APPDATA%\Echo\fe).</summary>
    public static string ExtractRoot { get; set; } = Path.Combine(echo.Abstractions.Core.AppPaths.BaseDir, "fe");

    private static bool HasLoose(string dir) =>
        File.Exists(Path.Combine(dir, NativeFileName)) && File.Exists(Path.Combine(dir, "fe.q8"));

    private static bool HasEmbedded =>
        EmbeddedAssembly?.GetManifestResourceInfo("echo.fe.native") is not null
        && EmbeddedAssembly.GetManifestResourceInfo("echo.fe.q8") is not null;

    public static bool IsAvailable => HasLoose(BaseDir) || HasEmbedded;

    /// <summary>Returns a directory containing the native + weights, extracting embedded copies if needed.</summary>
    public static string ResolveDirectory()
    {
        if (HasLoose(BaseDir))
        {
            return BaseDir;
        }

        if (!HasEmbedded)
        {
            throw new FileNotFoundException("FastEnhancer library is not bundled in this build.");
        }

        var native = ReadResource("echo.fe.native");
        var weights = ReadResource("echo.fe.q8");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(native.Concat(weights).ToArray()))[..12];
        var dir = Path.Combine(ExtractRoot, hash);
        if (!HasLoose(dir))
        {
            Directory.CreateDirectory(dir);
            WriteAtomic(Path.Combine(dir, "fe.q8"), weights);
            WriteAtomic(Path.Combine(dir, NativeFileName), native);
        }

        return dir;
    }

    private static byte[] ReadResource(string name)
    {
        using var s = EmbeddedAssembly!.GetManifestResourceStream(name)!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static void WriteAtomic(string path, byte[] data)
    {
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }

    public static ISpeechEnhancer? TryGet(out string? error)
    {
        lock (Gate)
        {
            if (_instance is not null || _loadError is not null)
            {
                error = _loadError;
                return _instance;
            }

            try
            {
                var dir = ResolveDirectory();
                var lib = NativeLibrary.Load(Path.Combine(dir, NativeFileName));
                T Fn<T>(string name) where T : Delegate =>
                    Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, name));
                if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
                {
                    // Zen 4 can advertise broken AVX-512 (see bkamuz/ud): prefer AVX-VNNI, else AVX2.
                    var tier = Fn<TierFn>("echo_fe_force_tier");
                    if (tier("avxvnni") != 0)
                    {
                        tier("avx2");
                    }
                }

                var bytes = File.ReadAllBytes(Path.Combine(dir, "fe.q8"));
                var w = Marshal.AllocHGlobal(bytes.Length); // must outlive the engine (zero-copy refs)
                Marshal.Copy(bytes, 0, w, bytes.Length);
                var rc = Fn<Init>("echo_fe_init")(w, bytes.Length);
                if (rc != 0)
                {
                    Marshal.FreeHGlobal(w);
                    throw new InvalidOperationException($"fe_init failed ({rc}); CPU lacks required SIMD tier?");
                }

                var reset = Fn<VoidFn>("echo_fe_reset");
                _instance = new FastEnhancerNative(
                    w, Fn<Run>("echo_fe_run"), () => reset(), Fn<IntFn>("echo_fe_frame_size")(), Fn<IntFn>("echo_fe_sample_rate")());
            }
            catch (Exception ex)
            {
                _loadError = ex.Message;
            }

            error = _loadError;
            return _instance;
        }
    }
}
