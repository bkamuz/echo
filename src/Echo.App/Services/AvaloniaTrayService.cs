using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using echo.Abstractions.Platform;
using echo.App.Localization;
using echo.App.ViewModels;

namespace echo.App.Services;

public sealed class AvaloniaTrayService : ITrayStateService
{
    private readonly TrayIcon _tray;
    private readonly WindowIcon _idleIcon;
    private readonly WindowIcon _recordingIcon;
    private readonly WindowIcon _processingIcon;
    private readonly ITaskbarIconSync? _taskbarIconSync;
    private readonly LocalizationService _loc;
    private readonly byte[] _idleIconBytes;
    private readonly byte[] _recordingIconBytes;
    private readonly byte[] _processingIconBytes;
    private Window? _mainWindow;
    private DictationOverlayState _currentState = DictationOverlayState.Hidden;

    public AvaloniaTrayService(LocalizationService loc, ITaskbarIconSync? taskbarIconSync = null)
    {
        _loc = loc;
        _taskbarIconSync = taskbarIconSync;
        _idleIconBytes = LoadTrayIconBytes("sleep");
        _recordingIconBytes = LoadTrayIconBytes("listen");
        _processingIconBytes = LoadTrayIconBytes("processing");
        _idleIcon = LoadTrayIcon(_idleIconBytes);
        _recordingIcon = LoadTrayIcon(_recordingIconBytes);
        _processingIcon = LoadTrayIcon(_processingIconBytes);

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

        void TryAttachHandle()
        {
            var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle != IntPtr.Zero)
            {
                _taskbarIconSync?.Attach(handle);
            }
        }

        window.Opened += (_, _) => TryAttachHandle();
        if (window.IsVisible)
        {
            TryAttachHandle();
        }
    }

    public void SetState(DictationOverlayState state)
    {
        if (_currentState == state)
        {
            return;
        }

        _currentState = state;
        ApplyState(state);
    }

    private void ApplyTooltipForCurrentState() => ApplyState(_currentState, force: true);

    private void ApplyState(DictationOverlayState state, bool force = false)
    {
        var (icon, iconBytes, tooltipKey) = state switch
        {
            DictationOverlayState.Hidden => (_idleIcon, _idleIconBytes, "Loc.Tray.Ready"),
            DictationOverlayState.Recording => (_recordingIcon, _recordingIconBytes, "Loc.Tray.Listening"),
            DictationOverlayState.Processing => (_processingIcon, _processingIconBytes, "Loc.Tray.Processing"),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };

        var tooltip = _loc.Get(tooltipKey);
        _tray.Icon = icon;
        _tray.ToolTipText = tooltip;

        if (_mainWindow is null)
        {
            return;
        }

        var mainWindow = _mainWindow;

        void Apply()
        {
            if (!OperatingSystem.IsLinux())
            {
                mainWindow.Icon = icon;

                var handle = mainWindow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (handle != IntPtr.Zero)
                {
                    _taskbarIconSync?.Attach(handle);
                }

                _taskbarIconSync?.ApplyIcon(iconBytes, state, tooltip);
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
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
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private static void ExitApp()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            {
                return;
            }

            if (desktop.MainWindow is echo.App.MainWindow mainWindow)
            {
                mainWindow.ForceClose();
                return;
            }

            desktop.Shutdown();
        });
    }

    private static byte[] LoadTrayIconBytes(string name)
    {
        var uri = new Uri($"avares://echo.App/Resources/{name}.png");
        using var stream = AssetLoader.Open(uri);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static WindowIcon LoadTrayIcon(byte[] bytes)
    {
        return new WindowIcon(new MemoryStream(bytes));
    }
}
