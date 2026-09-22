using echo.Abstractions.Core;

namespace echo.Platform.Windows;

/// <summary>
/// File-presence checks for Parakeet NPU runtime/model without loading Echo.Engines.ParakeetNpu
/// or Microsoft.ML.OnnxRuntime (which can native-abort when bundled ORT natives are absent).
/// </summary>
internal static class ParakeetRuntimeProbe
{
    private static readonly string[] RequiredRuntimeFiles =
    [
        "onnxruntime.dll",
        "onnxruntime_providers_qnn.dll",
        "QnnHtp.dll",
        "QnnHtpPrepare.dll",
        "QnnHtpNetRunExtensions.dll",
        "QnnHtpV73Stub.dll",
        "libQnnHtpV73Skel.so",
        "libqnnhtpv73.cat",
        "QnnSystem.dll",
    ];

    private static readonly string[] RequiredModelFiles =
    [
        "encoder-model.onnx",
        "encoder-model.bin",
        "decoder_joint-model.int8.onnx",
        "nemo128.onnx",
    ];

    public static bool IsRuntimeInstalled =>
        RequiredRuntimeFiles.All(file => IsPresent(AppPaths.NpuDir, file));

    public static bool IsModelInstalled =>
        RequiredModelFiles.All(file => IsPresent(AppPaths.ParakeetNpuDir, file));

    private static bool IsPresent(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }
}
