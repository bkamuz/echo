using echo.Abstractions.Core;
using echo.Abstractions.Engines;
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
        services.AddSingleton<ICursorPosition, WindowsCursorPosition>();
        services.AddSingleton<IDirectMlAvailability, WindowsDirectMlAvailability>();
        services.AddSingleton<INpuAvailability, WindowsNpuAvailability>();
        SherpaEnginesAssemblyLoader.RegisterEngines(services);
        if (ParakeetAssemblyLoader.IsSupported)
        {
            ParakeetAssemblyLoader.RegisterEngine(services);
            services.AddSingleton<IParakeetNpuModelSupport, WindowsParakeetNpuProbe>();
        }
        services.AddHttpClient<DirectMlRuntimeInstaller>();
        services.AddSingleton<IAutoStartService, WindowsAutoStartService>();
        services.AddHttpClient<IUpdateApplier, WindowsUpdateApplier>();
        return services;
    }
}
