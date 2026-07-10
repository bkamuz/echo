using Avalonia;
using Avalonia.Controls;
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

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Activated += OnActivated;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        ApplyLinuxChrome();
        EnsureKeyboardFocus();
        if (OperatingSystem.IsLinux())
        {
            App.Services.GetRequiredService<DictationCoordinator>().RestartHotkey();
            var hotkey = App.Services.GetRequiredService<IHotkeyService>();
            if (!hotkey.IsActive)
            {
                var message = LinuxHotkeySetup.GetSetupMessage();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    App.Services.GetRequiredService<AppStatusViewModel>().SetStatus(message);
                }
            }
        }

        await TryShowDependencyPromptAsync();
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            EnsureKeyboardFocus();
        }
    }

    private void ApplyLinuxChrome()
    {
        if (_linuxChromeApplied || !OperatingSystem.IsLinux())
        {
            return;
        }

        _linuxChromeApplied = true;

        // Linux window managers draw their own title bar; hide it and use the in-app chrome.
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = false;
        ClientCaptionButtons.IsVisible = true;
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
