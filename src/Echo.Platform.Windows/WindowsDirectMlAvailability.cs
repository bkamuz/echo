using echo.Abstractions.Platform;

namespace echo.Platform.Windows;

public sealed class WindowsDirectMlAvailability : IDirectMlAvailability
{
    private readonly Lazy<bool> _isAvailable;

    public WindowsDirectMlAvailability()
    {
        _isAvailable = new Lazy<bool>(Probe);
    }

    public bool IsAvailable => _isAvailable.Value;

    private static bool Probe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var baseDir = AppContext.BaseDirectory;
        var markerPath = Path.Combine(baseDir, "directml.enabled");
        if (!File.Exists(markerPath))
        {
            return false;
        }

        var sherpaApi = Path.Combine(baseDir, "sherpa-onnx-c-api.dll");
        if (!File.Exists(sherpaApi))
        {
            return false;
        }

        return OrtDirectMlExport.IsPresent(baseDir);
    }
}
