using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using echo.App.Services;
using echo.App.ViewModels;
using echo.Core;
using echo.Core.DependencyInjection;
using echo.Engines.DependencyInjection;
using echo.App.DependencyInjection;
using echo.Abstractions.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace echo.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.UseEcho();
                services.UsePlatform();
                services.UseEchoEngines();
                services.AddSingleton<AppStatusViewModel>();
                services.AddSingleton<SettingsApplyService>();
                services.AddSingleton<HomeViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<HistoryViewModel>();
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<ITrayStateService>(sp =>
                    new AvaloniaTrayService(sp.GetService<ITaskbarIconSync>()));
            })
            .Build();

        Services = host.Services;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var coordinator = Services.GetRequiredService<DictationCoordinator>();
            coordinator.Start();

            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<ShellViewModel>(),
            };
            if (Services.GetRequiredService<ITrayStateService>() is AvaloniaTrayService tray)
            {
                tray.AttachMainWindow(desktop.MainWindow);
            }
            desktop.Exit += (_, _) =>
            {
                coordinator.Stop();
                coordinator.Dispose();
                host.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
