using echo.Abstractions.Engines;
using Microsoft.Extensions.DependencyInjection;

namespace echo.Engines.DependencyInjection;

public static class EnginesServiceCollectionExtensions
{
    public static IServiceCollection UseEchoEngines(this IServiceCollection services)
    {
#if INCLUDE_WHISPER
        services.AddSingleton<Whisper.WhisperEngine>();
        services.AddSingleton<IWhisperModelSupport, Whisper.WhisperModelSupport>();
        services.AddSingleton(new EngineRegistration
        {
            EngineId = "whisper",
            Factory = sp => sp.GetRequiredService<Whisper.WhisperEngine>(),
        });
#endif
        services.AddSingleton<GigaAm.GigaAmEngine>();
        services.AddSingleton<Omnilingual.OmnilingualEngine>();
        services.AddSingleton(new EngineRegistration
        {
            EngineId = "gigaam",
            Factory = sp => sp.GetRequiredService<GigaAm.GigaAmEngine>(),
        });
        services.AddSingleton(new EngineRegistration
        {
            EngineId = "omnilingual",
            Factory = sp => sp.GetRequiredService<Omnilingual.OmnilingualEngine>(),
        });
        return services;
    }
}
