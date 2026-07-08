using echo.Abstractions.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace echo.Platform.Windows.DependencyInjection;

public static class WindowsServiceCollectionExtensions
{
    public static IServiceCollection AddWindowsPlatform(this IServiceCollection services)
    {
        services.AddSingleton<IAudioCapture, WasapiAudioCapture>();
        services.AddSingleton<IHotkeyService, WindowsHotkeyService>();
        services.AddSingleton<ITextInjector, WindowsTextInjector>();
        services.AddSingleton<IFocusTarget, WindowsFocusTarget>();
        services.AddSingleton<IDirectMlAvailability, WindowsDirectMlAvailability>();
        return services;
    }
}
