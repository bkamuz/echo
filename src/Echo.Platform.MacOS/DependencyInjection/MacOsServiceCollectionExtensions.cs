using echo.Abstractions.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace echo.Platform.MacOS.DependencyInjection;

public static class MacOsServiceCollectionExtensions
{
    public static IServiceCollection AddMacOsPlatform(this IServiceCollection services)
    {
        services.AddSingleton<IAudioCapture, MacOsAudioCapture>();
        services.AddSingleton<IHotkeyService, MacOsHotkeyService>();
        services.AddSingleton<ITextInjector, MacOsTextInjector>();
        services.AddSingleton<IFocusTarget, MacOsFocusTarget>();
        services.AddSingleton<IDirectMlAvailability, UnsupportedDirectMlAvailability>();
        services.AddSingleton<IAutoStartService, MacOsAutoStartService>();
        services.AddSingleton<IUpdateApplier, UnsupportedUpdateApplier>();
        return services;
    }
}
