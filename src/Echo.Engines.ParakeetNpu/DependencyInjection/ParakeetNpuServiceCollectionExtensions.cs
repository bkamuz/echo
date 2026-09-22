using System.Runtime.InteropServices;
using echo.Abstractions.Engines;
using Microsoft.Extensions.DependencyInjection;

namespace echo.Engines.ParakeetNpu.DependencyInjection;

[Obsolete("Parakeet is registered lazily via echo.Platform.Windows.ParakeetAssemblyLoader.")]
public static class ParakeetNpuServiceCollectionExtensions
{
    public static IServiceCollection AddParakeetNpuEngine(this IServiceCollection services)
    {
        if (!OperatingSystem.IsWindows()
            || RuntimeInformation.ProcessArchitecture is not Architecture.Arm64 and not Architecture.Arm)
        {
            return services;
        }

        services.AddHttpClient<QnnRuntimeDownloader>();
        services.AddHttpClient<ParakeetModelDownloader>();
        services.AddSingleton<IParakeetNpuModelSupport, ParakeetNpuModelSupport>();
        services.AddSingleton<ITranscriptionEngine, ParakeetNpuEngine>();
        return services;
    }
}
