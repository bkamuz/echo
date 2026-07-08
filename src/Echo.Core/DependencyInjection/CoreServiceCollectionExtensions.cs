using echo.Core;
using Microsoft.Extensions.DependencyInjection;

namespace echo.Core.DependencyInjection;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection UseEcho(this IServiceCollection services)
    {
        services.AddSingleton<ConfigStore>();
        services.AddSingleton<HistoryStore>();
        services.AddSingleton<TranscriptionService>();
        services.AddSingleton<DictationCoordinator>();
        services.AddHttpClient<ModelDownloader>();
        return services;
    }
}
