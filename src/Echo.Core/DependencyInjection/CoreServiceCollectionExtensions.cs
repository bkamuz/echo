using echo.Abstractions.Core;
using echo.Abstractions.Platform;
using echo.Core;
using echo.Core.Update;
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
        services.AddHttpClient<IUpdateChecker, GitHubUpdateChecker>((_, client) =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"Echo/{UpdateEnvironment.CurrentVersion}");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        });
        return services;
    }
}
