using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using echo.Abstractions.Platform;
using echo.App.DependencyInjection;
using echo.App.Services;
using echo.App.ViewModels;
using echo.Core;
using echo.Core.DependencyInjection;
using echo.Engines.DependencyInjection;
using echo.Platform.Linux;
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
                services.AddSingleton<IUserStatusNotifier, AppStatusNotifier>();
                services.AddSingleton<SettingsApplyService>();
                services.AddSingleton<LinuxDependencyPromptService>();
                services.AddSingleton<HomeViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<HistoryViewModel>();
                services.AddSingleton<UpdateViewModel>();
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<ITrayStateService>(sp =>
                    new AvaloniaTrayService(sp.GetService<ITaskbarIconSync>()));
            })
            .Build();

        Services = host.Services;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var coordinator = Services.GetRequiredService<DictationCoordinator>();
            var autoStart = Services.GetRequiredService<IAutoStartService>();
            if (autoStart.IsSupported && autoStart.IsEnabled != coordinator.Config.StartWithSystem)
            {
                autoStart.SetEnabled(coordinator.Config.StartWithSystem);
            }

            coordinator.Start();

            var status = Services.GetRequiredService<AppStatusViewModel>();
            if (OperatingSystem.IsLinux())
            {
                LinuxPlatformCapabilities.Refresh();
                status.SetPlatformWarning(LinuxPlatformCapabilities.StartupWarning);
            }
            else
            {
                status.RefreshReadiness();
            }

            var startMinimized = (desktop.Args ?? [])
                .Any(arg => string.Equals(arg, ApplicationLauncher.MinimizedArgument, StringComparison.OrdinalIgnoreCase));

            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<ShellViewModel>(),
            };

            if (startMinimized)
            {
                desktop.MainWindow.ShowInTaskbar = false;
                desktop.MainWindow.Opened += (_, _) => desktop.MainWindow.Hide();
            }

            if (Services.GetRequiredService<ITrayStateService>() is AvaloniaTrayService tray)
            {
                tray.AttachMainWindow(desktop.MainWindow);
            }

            if (OperatingSystem.IsLinux())
            {
                LinuxApplicationClipboardBridge.Register(
                    new AvaloniaApplicationClipboard(() => desktop.MainWindow));
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
