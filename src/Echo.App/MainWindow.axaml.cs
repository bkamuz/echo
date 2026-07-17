using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using echo.App.Services;
using echo.App.ViewModels;
using echo.Core;
using echo.Abstractions.Platform;
using echo.Platform.Linux;
using Microsoft.Extensions.DependencyInjection;

namespace echo.App;

public partial class MainWindow : Window
{
    private bool _linuxChromeApplied;
    private bool _dependencyPromptShown;
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();
        ApplyWindowsChrome();
        Opened += OnOpened;
        Activated += OnActivated;
        Closing += OnClosing;
    }

    /// <summary>
    /// Closes the window for real (tray Exit / app shutdown). Normal close hides to tray.
    /// </summary>
    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        ApplyLinuxChrome();
        EnsureKeyboardFocus();
        if (OperatingSystem.IsLinux())
        {
            App.Services.GetRequiredService<DictationCoordinator>().RestartHotkey();
        }

        await TryShowDependencyPromptAsync();
        App.Services.GetRequiredService<AppStatusViewModel>().RefreshReadiness();
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            EnsureKeyboardFocus();
        }
    }

    private void ApplyWindowsChrome()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        TitleBarGrid.ColumnDefinitions = new ColumnDefinitions("*,Auto,0");
        UpdateButtonPanel.Margin = new Thickness(0, 0, 140, 0);
    }

    private void ApplyLinuxChrome()
    {
        if (_linuxChromeApplied || !OperatingSystem.IsLinux())
        {
            return;
        }

        _linuxChromeApplied = true;

        // Linux: no system frame; our gray outline matches in-app separators.
        WindowDecorations = WindowDecorations.None;
        ExtendClientAreaToDecorationsHint = false;
        ClientCaptionButtons.IsVisible = true;
        ShellFrame.BorderThickness = new Thickness(1);
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2 && OperatingSystem.IsLinux())
        {
            OnMaximizeClick(sender, e);
            e.Handled = true;
            return;
        }

        BeginMoveDrag(e);
    }

    private void EnsureKeyboardFocus()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        Activate();
        if (!FocusFirstFocusableDescendant(ContentHost))
        {
            ContentHost.Focus();
        }
    }

    private static bool FocusFirstFocusableDescendant(Control root)
    {
        foreach (var child in root.GetVisualDescendants().OfType<Control>())
        {
            if (child.Focusable && child.IsEffectivelyVisible && child.IsEnabled)
            {
                return child.Focus();
            }
        }

        return false;
    }

    private async Task TryShowDependencyPromptAsync()
    {
        if (_dependencyPromptShown || !OperatingSystem.IsLinux() || !IsVisible)
        {
            return;
        }

        _dependencyPromptShown = true;
        var prompt = App.Services.GetRequiredService<LinuxDependencyPromptService>();
        await prompt.TryPromptAsync(this);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty && WindowState == WindowState.FullScreen)
        {
            WindowState = WindowState.Maximized;
        }
    }
}
