using echo.Abstractions.Platform;

namespace echo.Platform.Windows;

public sealed class WindowsDirectMlAvailability : IDirectMlAvailability
{
    public bool IsAvailable => Probe();

    private static bool Probe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var baseDir = DirectMlPaths.ResolveDirectory();
        return baseDir is not null && OrtDirectMlExport.IsPresent(baseDir);
    }
}
