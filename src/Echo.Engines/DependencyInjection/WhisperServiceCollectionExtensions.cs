using echo.Abstractions.Engines;
using Microsoft.Extensions.DependencyInjection;

namespace echo.Engines.DependencyInjection;

public static class WhisperServiceCollectionExtensions
{
    public static IServiceCollection UseWhisper(this IServiceCollection services)
    {
        services.AddSingleton<ITranscriptionEngine, Whisper.WhisperEngine>();
        return services;
    }
}
