using System.Security.Cryptography;

namespace echo.Core.Tests;

public class WinArm64PortableNativeStackTests
{
    /// <summary>org.k2fsa.sherpa.onnx.runtime.win-arm64/1.13.8 onnxruntime.dll</summary>
    public const string SherpaOnnxRuntimeSha256 =
        "968d081836cad9af537105f59e34d4997bca2ce6d78ff39d3e47660620e8a642";

    /// <summary>org.k2fsa.sherpa.onnx.runtime.win-arm64/1.13.8 sherpa-onnx-c-api.dll</summary>
    public const string SherpaCApiSha256 =
        "affb939df967d69d509aa6ce6b7c69473c1a35bdd5385ab845e0123c017277a3";

    /// <summary>Microsoft.ML.OnnxRuntime/1.24.4 win-arm64 onnxruntime.dll — must not ship in Echo.App.</summary>
    public const string MlOnnxRuntimeSha256 =
        "724d81ac50b11bfaa01ab3ce01b99fb6734a4b762259a8be4395ca662ce99fe6";

    public const int SherpaOnnxRuntimeSize = 17_635_328;
    public const int SherpaCApiSize = 4_584_960;
    public const int MlOnnxRuntimeSize = 14_215_752;

    [Fact]
    public void AnalyzePortableExe_DocumentsBrokenV1_9_15Stack()
    {
        var broken = Environment.GetEnvironmentVariable("ECHO_WIN_ARM64_BROKEN_EXE");
        if (string.IsNullOrWhiteSpace(broken) || !File.Exists(broken))
        {
            return;
        }

        var analysis = AnalyzePortableExe(broken);
        Assert.True(analysis.HasMlOnnxRuntime, "v1.9.15 ships ML.ORT instead of Sherpa ORT");
        Assert.False(analysis.IsValidSherpaStack);
        Assert.True(analysis.HasSherpaCApi);
    }

    [Fact]
    public void AnalyzePortableExe_DocumentsKnownGoodV1_9_7Stack()
    {
        var good = Environment.GetEnvironmentVariable("ECHO_WIN_ARM64_GOOD_EXE");
        if (string.IsNullOrWhiteSpace(good) || !File.Exists(good))
        {
            return;
        }

        var analysis = AnalyzePortableExe(good);
        Assert.True(analysis.IsValidSherpaStack, "v1.9.7 bundles Sherpa runtime (compressed single-file)");
        Assert.False(analysis.HasMlOnnxRuntime);
    }

    internal static PortableNativeStackAnalysis AnalyzePortableExe(string exePath)
    {
        var data = File.ReadAllBytes(exePath);
        return new PortableNativeStackAnalysis
        {
            HasSherpaOnnxRuntime = ContainsExactPeBlob(data, SherpaOnnxRuntimeSize, SherpaOnnxRuntimeSha256),
            HasSherpaCApi = ContainsExactPeBlob(data, SherpaCApiSize, SherpaCApiSha256),
            HasMlOnnxRuntime = ContainsExactPeBlob(data, MlOnnxRuntimeSize, MlOnnxRuntimeSha256),
            HasSherpaRuntimePackageRef = ContainsAscii(data, "org.k2fsa.sherpa.onnx.runtime.win-arm64"),
            QnnProviderStringRefs = CountOccurrences(data, "onnxruntime_providers_qnn"u8),
        };
    }

    private static bool ContainsExactPeBlob(byte[] data, int size, string expectedSha256)
    {
        if (data.Length < size)
        {
            return false;
        }

        for (var i = 0; i < data.Length - 1; i++)
        {
            if (data[i] != (byte)'M' || data[i + 1] != (byte)'Z')
            {
                continue;
            }

            if (i + size > data.Length)
            {
                continue;
            }

            var hash = Convert.ToHexString(SHA256.HashData(data.AsSpan(i, size))).ToLowerInvariant();
            if (hash == expectedSha256)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAscii(byte[] data, string text) =>
        data.AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes(text)) >= 0;

    private static int CountOccurrences(byte[] data, ReadOnlySpan<byte> needle)
    {
        var count = 0;
        var start = 0;
        while (start < data.Length)
        {
            var idx = data.AsSpan(start).IndexOf(needle);
            if (idx < 0)
            {
                break;
            }

            count++;
            start += idx + needle.Length;
        }

        return count;
    }

    internal sealed record PortableNativeStackAnalysis
    {
        public required bool HasSherpaOnnxRuntime { get; init; }

        public required bool HasSherpaCApi { get; init; }

        public required bool HasMlOnnxRuntime { get; init; }

        public required int QnnProviderStringRefs { get; init; }

        public bool HasSherpaRuntimePackageRef { get; init; }

        public bool IsValidSherpaStack =>
            !HasMlOnnxRuntime
            && (HasSherpaOnnxRuntime || HasSherpaRuntimePackageRef)
            && (HasSherpaCApi || HasSherpaRuntimePackageRef);
    }
}
