namespace echo.Platform.Windows;

internal static class DirectMlPaths
{
    public static string? ResolveDirectory()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "directml");
        return File.Exists(Path.Combine(dir, "sherpa-onnx-c-api.dll")) ? dir : null;
    }
}
