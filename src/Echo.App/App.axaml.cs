using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using echo.Abstractions.Core;
using echo.Abstractions.Platform;
using echo.App.DependencyInjection;
using echo.App.Localization;
using echo.App.Services;
using echo.App.ViewModels;
using echo.App.Views;
using echo.Core;
using echo.Core.DependencyInjection;
using echo.Engines.DependencyInjection;
using echo.Platform.Linux;
using echo.Platform.Windows;
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
                services.AddSingleton<LocalizationService>();
                services.AddSingleton<AppStatusViewModel>();
                services.AddSingleton<IUserStatusNotifier, AppStatusNotifier>();
                services.AddSingleton<IDictationResultNotifier, DictationToastService>();
                services.AddSingleton<SettingsApplyService>();
                services.AddSingleton<HotkeyCaptureController>();
                services.AddSingleton<LinuxDependencyPromptService>();
                services.AddSingleton<HomeViewModel>();
                services.AddSingleton<ModelSettingsController>();
                services.AddSingleton(sp => new SettingsViewModel(
                    sp.GetRequiredService<DictationCoordinator>(),
                    sp.GetRequiredService<IAudioCapture>(),
                    sp.GetRequiredService<HomeViewModel>(),
                    sp.GetRequiredService<AppStatusViewModel>(),
                    sp.GetRequiredService<SettingsApplyService>(),
                    sp.GetRequiredService<IDirectMlAvailability>(),
                    sp.GetRequiredService<IAutoStartService>(),
                    sp.GetRequiredService<HotkeyCaptureController>(),
                    sp.GetRequiredService<ModelSettingsController>(),
                    sp.GetServices<echo.Abstractions.Engines.ITranscriptionEngine>(),
                    sp.GetRequiredService<LocalizationService>(),
                    sp.GetService<DirectMlRuntimeInstaller>()));
                services.AddSingleton<HistoryViewModel>();
                services.AddSingleton<UpdateViewModel>();
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<DictationCursorOverlayService>();
                services.AddSingleton<ITrayStateService>(sp =>
                    new AvaloniaTrayService(
                        sp.GetRequiredService<LocalizationService>(),
                        sp.GetRequiredService<DictationCursorOverlayService>()));
            })
            .Build();

        Services = host.Services;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var coordinator = Services.GetRequiredService<DictationCoordinator>();
            var loc = Services.GetRequiredService<LocalizationService>();
            loc.Apply(coordinator.Config.UiLanguage);

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
            else
            {
                desktop.MainWindow.Opened += (_, _) => _ = MaybeShowFirstRunAsync(desktop.MainWindow);
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
                try
                {
                    coordinator.Stop();
                    coordinator.Dispose();
                }
                catch
                {
                    // Best-effort stop before host dispose.
                }

                // Native engine dispose (DirectML/Sherpa) can block for minutes on the UI thread.
                var disposeTask = Task.Run(() =>
                {
                    try
                    {
                        host.Dispose();
                    }
                    catch
                    {
                        // Ignore dispose failures on shutdown.
                    }
                });

                if (!disposeTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    Environment.Exit(0);
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task MaybeShowFirstRunAsync(Window mainWindow)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var coordinator = Services.GetRequiredService<DictationCoordinator>();
            var config = coordinator.Config;
            var spec = ModelRegistry.SpecForEngine(config.Engine, config.WhisperModelSize, config.GigaAmModelSize);
            if (spec is null || spec.IsDownloaded())
            {
                return;
            }

            var settings = Services.GetRequiredService<SettingsViewModel>();
            var dialog = new FirstRunDialog(settings, coordinator);
            await dialog.ShowDialog(mainWindow);
            Services.GetRequiredService<HomeViewModel>().NotifyConfigChanged();
            Services.GetRequiredService<AppStatusViewModel>().RefreshReadiness();
        });
    }
}
