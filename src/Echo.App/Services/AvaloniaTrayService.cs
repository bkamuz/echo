using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using echo.Abstractions.Platform;
using echo.App.Localization;
using echo.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace echo.App.Services;

public sealed class AvaloniaTrayService : ITrayStateService
{
    private readonly TrayIcon _tray;
    private readonly WindowIcon _idleIcon;
    private readonly WindowIcon _recordingIcon;
    private readonly WindowIcon _processingIcon;
    private readonly DictationCursorOverlayService _cursorOverlay;
    private readonly LocalizationService _loc;
    private Window? _mainWindow;
    private DictationOverlayState _currentState = DictationOverlayState.Hidden;
    private bool _isExiting;

    public AvaloniaTrayService(
        LocalizationService loc,
        DictationCursorOverlayService cursorOverlay)
    {
        _loc = loc;
        _cursorOverlay = cursorOverlay;
        _idleIcon = LoadTrayIcon("sleep");
        _recordingIcon = LoadTrayIcon("listen");
        _processingIcon = LoadTrayIcon("processing");

        _tray = new TrayIcon
        {
            Icon = _idleIcon,
            ToolTipText = _loc.Get("Loc.Tray.Ready"),
            IsVisible = true,
            Menu = BuildMenu(),
        };

        _loc.LanguageChanged += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_isExiting)
                {
                    return;
                }

                _tray.Menu = BuildMenu();
                ApplyTooltipForCurrentState();
            });
        };
    }

    public void AttachMainWindow(Window window)
    {
        _mainWindow = window;
        window.Icon = _idleIcon;

        _tray.Clicked += (_, _) => ShowMainWindow();
    }

    public void SetState(DictationOverlayState state)
    {
        if (_currentState == state)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            _currentState = state;
            ApplyState(state);
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_currentState == state)
            {
                return;
            }

            _currentState = state;
            ApplyState(state);
        });
    }

    private void ApplyTooltipForCurrentState() => ApplyState(_currentState);

    private void ApplyState(DictationOverlayState state)
    {
        if (_isExiting)
        {
            return;
        }

        var (icon, tooltipKey) = state switch
        {
            DictationOverlayState.Hidden => (_idleIcon, "Loc.Tray.Ready"),
            DictationOverlayState.Recording => (_recordingIcon, "Loc.Tray.Listening"),
            DictationOverlayState.Processing => (_processingIcon, "Loc.Tray.Processing"),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };

        _tray.Icon = icon;
        _tray.ToolTipText = _loc.Get(tooltipKey);
        _cursorOverlay.SetState(state);
    }

    private NativeMenu BuildMenu()
    {
        var open = new NativeMenuItem(_loc.Get("Loc.Tray.Open"));
        open.Click += (_, _) => ShowMainWindow();

        var settings = new NativeMenuItem(_loc.Get("Loc.Tray.Settings"));
        settings.Click += (_, _) => OpenPage(AppPage.Settings);

        var history = new NativeMenuItem(_loc.Get("Loc.Tray.History"));
        history.Click += (_, _) => OpenPage(AppPage.History);

        var exit = new NativeMenuItem(_loc.Get("Loc.Tray.Exit"));
        exit.Click += (_, _) => ExitApp();

        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(settings);
        menu.Items.Add(history);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exit);
        return menu;
    }

    private void OpenPage(AppPage page)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_mainWindow?.DataContext is ShellViewModel shell)
            {
                shell.NavigateCommand.Execute(page);
            }

            ShowMainWindowCore();
        });
    }

    private void ShowMainWindow()
    {
        Dispatcher.UIThread.Post(ShowMainWindowCore);
    }

    private void ShowMainWindowCore()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.ShowInTaskbar = true;
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Show();
        _mainWindow.Activate();

        // After update restart (--minimized / prior Hidden start) Win32 may leave the
        // window inactive; a brief Topmost nudge brings it to the foreground.
        if (OperatingSystem.IsWindows())
        {
            _mainWindow.Topmost = true;
            _mainWindow.Topmost = false;
        }
    }

    private void ExitApp()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_isExiting)
            {
                return;
            }

            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            {
                return;
            }

            _isExiting = true;
            _tray.ToolTipText = _loc.Get("Loc.Tray.Closing");
            _tray.Menu = null;

            try
            {
                App.Services.GetRequiredService<AppStatusViewModel>()
                    .SetStatus("Loc.Status.Closing", busy: true);
            }
            catch
            {
                // Status bar may be unavailable during early/late lifecycle.
            }

            if (desktop.MainWindow is echo.App.MainWindow mainWindow)
            {
                mainWindow.ForceClose();
            }

            desktop.Shutdown();
        });
    }

    private static WindowIcon LoadTrayIcon(string name)
    {
        var uri = new Uri($"avares://echo.App/Resources/{name}.png");
        using var stream = AssetLoader.Open(uri);
        return new WindowIcon(stream);
    }
}
