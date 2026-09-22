using echo.Abstractions.Engines;
using Microsoft.Extensions.DependencyInjection;

namespace echo.Engines.DependencyInjection;

public static class EnginesServiceCollectionExtensions
{
    public static IServiceCollection UseEchoEngines(this IServiceCollection services)
    {
#if INCLUDE_WHISPER
        services.AddSingleton<IWhisperModelSupport, Whisper.WhisperModelSupport>();
        services.AddSingleton(new EngineRegistration
        {
            EngineId = "whisper",
            Factory = sp => new Whisper.WhisperEngine(
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Whisper.WhisperEngine>>()),
        });
#endif
        services.AddSingleton(new EngineRegistration
        {
            EngineId = "gigaam",
            Factory = sp => new GigaAm.GigaAmEngine(
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GigaAm.GigaAmEngine>>()),
        });
        services.AddSingleton(new EngineRegistration
        {
            EngineId = "omnilingual",
            Factory = sp => new Omnilingual.OmnilingualEngine(
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Omnilingual.OmnilingualEngine>>()),
        });
        return services;
    }
}
