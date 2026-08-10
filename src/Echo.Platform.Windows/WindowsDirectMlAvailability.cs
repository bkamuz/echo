using System.Runtime.InteropServices;
using echo.Abstractions.Platform;

namespace echo.Platform.Windows;

public sealed class WindowsDirectMlAvailability : IDirectMlAvailability
{
    /// <summary>
    /// GPU/DirectML natives are x64-only today. Hide the option on native ARM Windows.
    /// </summary>
    public bool IsAvailable =>
        OperatingSystem.IsWindows()
        && RuntimeInformation.ProcessArchitecture is not Architecture.Arm64 and not Architecture.Arm;
}
