using echo.Abstractions.Core;
using echo.Abstractions.Platform;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.Versioning;

namespace echo.Platform.Windows.DependencyInjection;

public static class WindowsServiceCollectionExtensions
{
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddWindowsPlatform(this IServiceCollection services)
    {
        services.AddSingleton<IAudioCapture, WasapiAudioCapture>();
        services.AddSingleton<IHotkeyService, WindowsHotkeyService>();
        services.AddSingleton<ITextInjector, WindowsTextInjector>();
        services.AddSingleton<IFocusTarget, WindowsFocusTarget>();
        services.AddSingleton<IDirectMlAvailability, WindowsDirectMlAvailability>();
        services.AddSingleton<ITaskbarIconSync, WindowsTaskbarIconSync>();
        services.AddSingleton<IAutoStartService, WindowsAutoStartService>();
        services.AddHttpClient<IUpdateApplier, WindowsUpdateApplier>();
        return services;
    }
}
