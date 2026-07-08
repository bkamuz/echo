using echo.Abstractions.Engines;
using Microsoft.Extensions.DependencyInjection;

namespace echo.Engines.DependencyInjection;

public static class GigaAmServiceCollectionExtensions
{
    public static IServiceCollection UseGigaAm(this IServiceCollection services)
    {
        services.AddSingleton<ITranscriptionEngine, GigaAm.GigaAmEngine>();
        return services;
    }
}
