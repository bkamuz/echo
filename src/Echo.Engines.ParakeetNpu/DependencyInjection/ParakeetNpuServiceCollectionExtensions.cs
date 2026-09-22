using echo.Abstractions.Engines;
using Microsoft.Extensions.DependencyInjection;

namespace echo.Engines.ParakeetNpu.DependencyInjection;

public static class ParakeetNpuServiceCollectionExtensions
{
    public static IServiceCollection AddParakeetNpuEngine(this IServiceCollection services)
    {
        if (!OperatingSystem.IsWindows())
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
