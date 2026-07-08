using System.Runtime.InteropServices;

namespace echo.Platform.Windows;

internal static class OrtDirectMlExport
{
    private const string OrtDll = "onnxruntime.dll";
    private const string DmlSymbol = "OrtSessionOptionsAppendExecutionProvider_DML";

    public static bool IsPresent(string directory)
    {
        var path = Path.Combine(directory, OrtDll);
        if (!File.Exists(path))
        {
            return false;
        }

        if (!NativeLibrary.TryLoad(path, out var handle))
        {
            return false;
        }

        try
        {
            return NativeLibrary.TryGetExport(handle, DmlSymbol, out _);
        }
        finally
        {
            NativeLibrary.Free(handle);
        }
    }
}
