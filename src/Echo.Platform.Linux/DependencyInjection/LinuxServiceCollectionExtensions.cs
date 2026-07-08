using echo.Abstractions.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace echo.Platform.Linux.DependencyInjection;

public static class LinuxServiceCollectionExtensions
{
    public static IServiceCollection AddLinuxPlatform(this IServiceCollection services)
    {
        services.AddSingleton<IAudioCapture, LinuxAudioCapture>();
        services.AddSingleton<IHotkeyService, LinuxHotkeyService>();
        services.AddSingleton<ITextInjector, LinuxTextInjector>();
        services.AddSingleton<IFocusTarget, LinuxFocusTarget>();
        return services;
    }
}
