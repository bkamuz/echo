using echo.Abstractions.Engines;
using Microsoft.Extensions.DependencyInjection;

namespace echo.Engines.DependencyInjection;

public static class EnginesServiceCollectionExtensions
{
    public static IServiceCollection UseEchoEngines(this IServiceCollection services)
    {
        services.AddSingleton<ITranscriptionEngine, Whisper.WhisperEngine>();
        services.AddSingleton<ITranscriptionEngine, GigaAm.GigaAmEngine>();
        services.AddSingleton<ITranscriptionEngine, Omnilingual.OmnilingualEngine>();
        return services;
    }
}
