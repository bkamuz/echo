namespace echo.Platform.Windows;

internal static class OrtQnnExport
{
    private const string OrtDll = "onnxruntime.dll";
    private const string QnnProviderDll = "onnxruntime_providers_qnn.dll";

    /// <summary>
    /// File-presence probe only — must not load native DLLs during settings/startup checks.
    /// </summary>
    public static bool IsPresent(string directory)
    {
        return File.Exists(Path.Combine(directory, OrtDll))
            && File.Exists(Path.Combine(directory, QnnProviderDll));
    }
}
