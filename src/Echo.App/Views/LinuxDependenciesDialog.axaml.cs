using Avalonia.Controls;
using Avalonia.Interactivity;
using echo.Abstractions.Platform;
using echo.Platform.Linux;

namespace echo.App.Views;

public partial class LinuxDependenciesDialog : Window
{
    private const string TestPhrase = "Echo — тест вставки.";

    private readonly IReadOnlyList<LinuxDependency> _dependencies;
    private readonly bool _needsInputGroup;
    private readonly bool _skipAccessibilityStep;
    private int _step;

    public LinuxDependenciesDialog(
        IReadOnlyList<LinuxDependency> dependencies,
        bool canInstall,
        bool needsInputGroup,
        string? extraMessage = null)
    {
        _dependencies = dependencies;
        _needsInputGroup = needsInputGroup;
        _skipAccessibilityStep = LinuxDependencyCatalog.UsesGnomeWaylandYdotool;
        InitializeComponent();
        DependenciesList.ItemsSource = dependencies;
        DependenciesList.IsVisible = dependencies.Count > 0;
        InstallButton.IsEnabled = canInstall && dependencies.Count > 0;
        InstallButton.IsVisible = dependencies.Count > 0;
        InputGroupButton.IsVisible = needsInputGroup;
        InputGroupButton.IsEnabled = LinuxPackageManagerDetector.CanElevateInstall()
            || LinuxInputGroupInstaller.IsCurrentUserInInputGroup();

        if (!string.IsNullOrWhiteSpace(extraMessage))
        {
            StatusText.IsVisible = true;
            StatusText.Text = extraMessage;
        }
        else if (!canInstall && dependencies.Count > 0)
        {
            StatusText.IsVisible = true;
            StatusText.Text = LinuxPlatformCapabilities.IsFlatpakSandbox
                ? "В Flatpak зависимости ставятся вместе с пакетом Echo или через менеджер пакетов системы."
                : "Автоустановка недоступна. Установите пакеты вручную через менеджер пакетов дистрибутива.";
        }
        else if (needsInputGroup)
        {
            StatusText.IsVisible = true;
            StatusText.Text = LinuxHotkeySetup.GetSetupMessage();
        }

        ShowStep(0);
    }

    public bool SkipPermanently => DontAskAgainCheckBox.IsChecked == true;

    public bool SetupCompleted { get; private set; }

    public bool InstallAttempted { get; private set; }

    private int LastStep => _skipAccessibilityStep ? 1 : 2;

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
            0 => $"Шаг 1 из {totalSteps} — Системные пакеты",
            _ when step == LastStep => $"Шаг {totalSteps} из {totalSteps} — Проверка вставки",
            _ => $"Шаг 2 из {totalSteps} — Специальные возможности",
        };

        StepSubtitle.Text = step switch
        {
            0 => GetPackagesStepSubtitle(),
            _ when step == LastStep => "Убедитесь, что вставка работает в вашем окружении.",
            _ => "Для автоматической вставки через AT-SPI нужен доступ к специальным возможностям.",
        };

        if (!_skipAccessibilityStep && step == 1)
        {
            AccessibilityInstructions.Text = LinuxAccessibilityGuide.GetInstructions();
            LimitationsText.Text = LinuxAccessibilityGuide.Limitations;
        }
    }

    private static string GetPackagesStepSubtitle()
    {
        if (LinuxDependencyCatalog.UsesGnomeWaylandYdotool)
        {
            return "На GNOME Wayland Echo использует ydotool для автовставки. "
                + "Установите ydotool, wl-clipboard и arecord.";
        }

        return "Echo подберёт способ вставки автоматически (AT-SPI, xdotool или wtype). "
            + "Установите недостающие компоненты.";
    }

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
        StatusText.Text = "Запрошены права администратора. Подтвердите установку в системном окне…";

        InstallAttempted = true;
        var result = await LinuxDependencyInstaller.TryInstallAsync(_dependencies).ConfigureAwait(true);

        StatusText.Text = result.Message;
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
        StatusText.Text = "Запрошены права администратора для добавления в группу input…";

        InstallAttempted = true;
        var result = await LinuxInputGroupInstaller.TryAddCurrentUserAsync().ConfigureAwait(true);

        StatusText.Text = result.Message;
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
            StatusText.Text = "Откройте настройки специальных возможностей вручную.";
        }
    }

    private async void OnTestClick(object? sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        TestResultText.IsVisible = true;
        TestResultText.Text = "Проверка…";

        try
        {
            var injector = new LinuxTextInjector();
            var result = await injector.InjectAsync(TestPhrase, "auto", 0).ConfigureAwait(true);
            TestResultText.Text = result.Outcome switch
            {
                TextInjectionOutcome.AutoPasted => "Успех — текст вставлен автоматически.",
                TextInjectionOutcome.ClipboardOnly =>
                    $"Частично — {result.Message ?? "текст скопирован в буфер."}",
                _ => $"Не удалось: {result.Message ?? "неизвестная ошибка"}",
            };
        }
        catch (Exception ex)
        {
            TestResultText.Text = $"Ошибка: {ex.Message}";
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }
}
