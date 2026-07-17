using Avalonia.Controls;
using Avalonia.Interactivity;
using echo.Abstractions.Platform;
using echo.App.Localization;
using echo.Platform.Linux;
using Microsoft.Extensions.DependencyInjection;

namespace echo.App.Views;

public sealed record LinuxDependencyView(string DisplayName, string Description);

public partial class LinuxDependenciesDialog : Window
{
    private readonly IReadOnlyList<LinuxDependency> _dependencies;
    private readonly LocalizationService _loc;
    private readonly bool _needsInputGroup;
    private readonly bool _skipAccessibilityStep;
    private int _step;

    public LinuxDependenciesDialog()
    {
        _dependencies = [];
        _loc = null!;
        InitializeComponent();
    }

    public LinuxDependenciesDialog(
        IReadOnlyList<LinuxDependency> dependencies,
        bool canInstall,
        bool needsInputGroup,
        string? extraMessage = null)
    {
        _dependencies = dependencies;
        _needsInputGroup = needsInputGroup;
        _skipAccessibilityStep = LinuxDependencyCatalog.UsesGnomeWaylandYdotool;
        _loc = App.Services.GetRequiredService<LocalizationService>();
        InitializeComponent();

        DependenciesList.ItemsSource = dependencies
            .Select(d => new LinuxDependencyView(_loc.Get(d.DisplayName), _loc.Get(d.Description)))
            .ToList();
        DependenciesList.IsVisible = dependencies.Count > 0;
        InstallButton.IsEnabled = canInstall && dependencies.Count > 0;
        InstallButton.IsVisible = dependencies.Count > 0;
        InputGroupButton.IsVisible = needsInputGroup;
        InputGroupButton.IsEnabled = LinuxPackageManagerDetector.CanElevateInstall()
            || LinuxInputGroupInstaller.IsCurrentUserInInputGroup();

        if (!string.IsNullOrWhiteSpace(extraMessage))
        {
            StatusText.IsVisible = true;
            StatusText.Text = Localize(extraMessage);
        }
        else if (!canInstall && dependencies.Count > 0)
        {
            StatusText.IsVisible = true;
            StatusText.Text = _loc.Get(
                LinuxPlatformCapabilities.IsFlatpakSandbox
                    ? "Loc.Linux.Dialog.FlatpakHint"
                    : "Loc.Linux.Dialog.ManualInstall");
        }
        else if (needsInputGroup)
        {
            StatusText.IsVisible = true;
            StatusText.Text = Localize(LinuxHotkeySetup.GetSetupMessage());
        }

        ShowStep(0);
    }

    public bool SkipPermanently => DontAskAgainCheckBox.IsChecked == true;

    public bool SetupCompleted { get; private set; }

    public bool InstallAttempted { get; private set; }

    private int LastStep => _skipAccessibilityStep ? 1 : 2;

    private string TestPhrase => _loc.Get("Loc.Linux.Dialog.TestPhrase");

    private void ShowStep(int step)
    {
        _step = step;
        PackagesPanel.IsVisible = step == 0;
        AccessibilityPanel.IsVisible = !_skipAccessibilityStep && step == 1;
        TestPanel.IsVisible = step == LastStep;

        BackButton.IsVisible = step > 0;
        NextButton.IsVisible = step < LastStep;
        FinishButton.IsVisible = step == LastStep;
        InstallButton.IsVisible = step == 0 && _dependencies.Count > 0;

        var totalSteps = LastStep + 1;
        StepTitle.Text = step switch
        {
            0 => _loc.Format("Loc.Linux.Dialog.StepPackages.Title", 1, totalSteps),
            _ when step == LastStep => _loc.Format("Loc.Linux.Dialog.StepTest.Title", totalSteps, totalSteps),
            _ => _loc.Format("Loc.Linux.Dialog.StepA11y.Title", 2, totalSteps),
        };

        StepSubtitle.Text = step switch
        {
            0 => GetPackagesStepSubtitle(),
            _ when step == LastStep => _loc.Get("Loc.Linux.Dialog.Test.Subtitle"),
            _ => _loc.Get("Loc.Linux.Dialog.A11y.Subtitle"),
        };

        if (!_skipAccessibilityStep && step == 1)
        {
            AccessibilityInstructions.Text = Localize(LinuxAccessibilityGuide.GetInstructions());
            LimitationsText.Text = Localize(LinuxAccessibilityGuide.Limitations);
        }
    }

    private string GetPackagesStepSubtitle() =>
        _loc.Get(
            LinuxDependencyCatalog.UsesGnomeWaylandYdotool
                ? "Loc.Linux.Dialog.Packages.Subtitle.GnomeDetail"
                : "Loc.Linux.Dialog.Packages.Subtitle.Detail");

    private void OnBackClick(object? sender, RoutedEventArgs e) => ShowStep(_step - 1);

    private void OnNextClick(object? sender, RoutedEventArgs e)
    {
        LinuxPlatformCapabilities.Refresh();
        ShowStep(_step + 1);
    }

    private void OnFinishClick(object? sender, RoutedEventArgs e)
    {
        SetupCompleted = true;
        Close(true);
    }

    private void OnSkipClick(object? sender, RoutedEventArgs e) => Close(false);

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        SkipButton.IsEnabled = false;
        InputGroupButton.IsEnabled = false;
        StatusText.IsVisible = true;
        StatusText.Text = _loc.Get("Loc.Linux.Dialog.AdminInstall");

        InstallAttempted = true;
        var result = await LinuxDependencyInstaller.TryInstallAsync(_dependencies).ConfigureAwait(true);

        StatusText.Text = Localize(result.Message);
        SkipButton.IsEnabled = true;
        InputGroupButton.IsEnabled = _needsInputGroup;
        LinuxPlatformCapabilities.Refresh();

        if (result.Succeeded)
        {
            InstallButton.IsVisible = false;
            return;
        }

        InstallButton.IsEnabled = LinuxPlatformCapabilities.CanAutoInstall
            && LinuxPlatformCapabilities.MissingDependencies.Count > 0;
    }

    private async void OnInputGroupClick(object? sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        SkipButton.IsEnabled = false;
        InputGroupButton.IsEnabled = false;
        StatusText.IsVisible = true;
        StatusText.Text = _loc.Get("Loc.Linux.Dialog.AdminInputGroup");

        InstallAttempted = true;
        var result = await LinuxInputGroupInstaller.TryAddCurrentUserAsync().ConfigureAwait(true);

        StatusText.Text = Localize(result.Message);
        SkipButton.IsEnabled = true;
        InputGroupButton.IsEnabled = _needsInputGroup;
        InstallButton.IsEnabled = LinuxPlatformCapabilities.CanAutoInstall
            && LinuxPlatformCapabilities.MissingDependencies.Count > 0;

        if (result.Succeeded && LinuxPlatformCapabilities.SupportsGlobalHotkey)
        {
            InputGroupButton.IsVisible = false;
        }
    }

    private void OnOpenAccessibilityClick(object? sender, RoutedEventArgs e)
    {
        if (!LinuxAccessibilityGuide.TryOpenSettings())
        {
            StatusText.IsVisible = true;
            StatusText.Text = _loc.Get("Loc.Linux.Dialog.OpenA11yManual");
        }
    }

    private async void OnTestClick(object? sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        TestResultText.IsVisible = true;
        TestResultText.Text = _loc.Get("Loc.Linux.Dialog.Testing");

        try
        {
            var injector = new LinuxTextInjector();
            var result = await injector.InjectAsync(TestPhrase, "auto", 0).ConfigureAwait(true);
            TestResultText.Text = result.Outcome switch
            {
                TextInjectionOutcome.AutoPasted => _loc.Get("Loc.Linux.Dialog.Test.Success"),
                TextInjectionOutcome.ClipboardOnly =>
                    _loc.Format(
                        "Loc.Linux.Dialog.Test.Partial",
                        Localize(result.Message ?? "Loc.Linux.Inject.ClipboardOnly")),
                _ => _loc.Format(
                    "Loc.Linux.Dialog.Test.Fail",
                    Localize(result.Message ?? "Loc.Inject.Failed")),
            };
        }
        catch (Exception ex)
        {
            TestResultText.Text = _loc.Format("Loc.Linux.Dialog.Test.Error", ex.Message);
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private string Localize(string? keyOrText) => _loc.LocText(keyOrText);
}
