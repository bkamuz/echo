using echo.Abstractions.Core;
using echo.Abstractions.Engines;
using echo.Core.Update;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace echo.Core.DependencyInjection;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection UseEcho(this IServiceCollection services)
    {
        services.AddSingleton<ConfigStore>();
        services.AddSingleton<HistoryStore>();
        services.AddSingleton<TranscriptionService>();
        services.AddSingleton<DictationCoordinator>();
        services.AddHttpClient("echo-models");
        services.AddSingleton(sp =>
        {
            var httpFactory = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>();
            return new ModelDownloader(
                sp.GetRequiredService<ILogger<ModelDownloader>>(),
                httpFactory.CreateClient("echo-models"),
                sp.GetService<IWhisperModelSupport>());
        });
        services.AddHttpClient<IUpdateChecker, GitHubUpdateChecker>((_, client) =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"Echo/{UpdateEnvironment.CurrentVersion}");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        });
        return services;
    }
}
