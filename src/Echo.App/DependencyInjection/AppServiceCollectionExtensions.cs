using Microsoft.Extensions.DependencyInjection;

namespace echo.App.DependencyInjection;

public static class AppServiceCollectionExtensions
{
    public static IServiceCollection UsePlatform(this IServiceCollection services)
    {
        if (OperatingSystem.IsWindows())
        {
            echo.Platform.Windows.DependencyInjection.WindowsServiceCollectionExtensions.AddWindowsPlatform(services);
        }
        else if (OperatingSystem.IsMacOS())
        {
            echo.Platform.MacOS.DependencyInjection.MacOsServiceCollectionExtensions.AddMacOsPlatform(services);
        }
        else if (OperatingSystem.IsLinux())
        {
            echo.Platform.Linux.DependencyInjection.LinuxServiceCollectionExtensions.AddLinuxPlatform(services);
        }
        else
        {
            throw new PlatformNotSupportedException("Unsupported operating system.");
        }

        return services;
    }
}
