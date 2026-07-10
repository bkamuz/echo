using Avalonia.Controls;
using Avalonia.Interactivity;
using echo.Platform.Linux;
using echo.Platform.Linux.Injection;

namespace echo.App.Views;

public partial class LinuxDependenciesDialog : Window
{
    private const string TestPhrase = "Echo — тест вставки.";

    private readonly IReadOnlyList<LinuxDependency> _dependencies;
    private readonly bool _needsInputGroup;
    private int _step;

    public LinuxDependenciesDialog(
        IReadOnlyList<LinuxDependency> dependencies,
        bool canInstall,
        bool needsInputGroup,
        string? extraMessage = null)
    {
        _dependencies = dependencies;
        _needsInputGroup = needsInputGroup;
        InitializeComponent();
        DependenciesList.ItemsSource = dependencies;
        DependenciesList.IsVisible = dependencies.Count > 0;
        InstallButton.IsEnabled = canInstall && dependencies.Count > 0;
        InstallButton.IsVisible = dependencies.Count > 0;
        InputGroupButton.IsVisible = needsInputGroup;
        InputGroupButton.IsEnabled = LinuxPackageManagerDetector.CanElevateInstall()
            || LinuxInputGroupInstaller.IsCurrentUserInInputGroup();
        UpdateAccessibilityStepContent();
        UpdateBackendsSummary();

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

    private void ShowStep(int step)
    {
        _step = step;
        PackagesPanel.IsVisible = step == 0;
        AccessibilityPanel.IsVisible = step == 1;
        TestPanel.IsVisible = step == 2;

        BackButton.IsVisible = step > 0;
        NextButton.IsVisible = step is 0 or 1;
        FinishButton.IsVisible = step == 2;
        InstallButton.IsVisible = step == 0 && _dependencies.Count > 0;

        StepTitle.Text = step switch
        {
            0 => "Шаг 1 из 3 — Системные пакеты",
            1 => LinuxDependencyCatalog.UsesGnomeWaylandYdotool
                ? "Шаг 2 из 3 — ydotool и группа input"
                : "Шаг 2 из 3 — Специальные возможности",
            _ => "Шаг 3 из 3 — Проверка вставки",
        };

        StepSubtitle.Text = step switch
        {
            0 => GetPackagesStepSubtitle(),
            1 => GetAccessibilityStepSubtitle(),
            _ => "Убедитесь, что вставка работает в вашем окружении.",
        };

        if (step == 1)
        {
            UpdateAccessibilityStepContent();
        }
    }

    private void UpdateAccessibilityStepContent()
    {
        AccessibilityInstructions.Text = LinuxAccessibilityGuide.GetInstructions();
        LimitationsText.Text = LinuxDependencyCatalog.UsesGnomeWaylandYdotool
            ? "ydotool работает в браузерах, офисных приложениях и большинстве полей ввода. "
              + "В играх, ncurses-терминалах и sandbox-приложениях останется только копирование в буфер."
            : LinuxAccessibilityGuide.Limitations;
        OpenAccessibilityButton.IsVisible = !LinuxDependencyCatalog.UsesGnomeWaylandYdotool;
    }

    private static string GetPackagesStepSubtitle()
    {
        if (LinuxDependencyCatalog.UsesGnomeWaylandYdotool)
        {
            return "На GNOME Wayland Echo использует ydotool для автовставки (Ctrl+V). "
                + "Пакеты AT-SPI не нужны — установите ydotool, wl-clipboard и arecord.";
        }

        return "Echo подберёт способ вставки автоматически (AT-SPI, ydotool, xdotool или wtype). "
            + "Установите недостающие компоненты.";
    }

    private static string GetAccessibilityStepSubtitle()
    {
        if (LinuxDependencyCatalog.UsesGnomeWaylandYdotool)
        {
            return "На GNOME Wayland автовставка идёт через ydotool — нужны ydotoold и группа input. "
                + "AT-SPI для вставки здесь не используется.";
        }

        return "Для автоматической вставки через AT-SPI нужен доступ к специальным возможностям.";
    }

    private void UpdateBackendsSummary()
    {
        var probes = LinuxInjectionChain.ProbeBackends();
        var available = probes.Where(probe => probe.Available).Select(probe => probe.Name).ToList();
        if (available.Count > 0)
        {
            var prefix = LinuxDependencyCatalog.UsesGnomeWaylandYdotool
                ? "На GNOME Wayland автовставка через ydotool (AT-SPI не используется). "
                : string.Empty;
            BackendsText.Text = $"{prefix}Доступные способы вставки: {string.Join(", ", available)}.";
            return;
        }

        BackendsText.Text = LinuxDependencyCatalog.UsesGnomeWaylandYdotool
            ? "Пока нет рабочих способов автовставки — установите ydotool и wl-clipboard, запустите ydotoold и добавьте себя в группу input."
            : "Пока нет рабочих способов автовставки — установите пакеты и включите спец. возможности.";
    }

    private void OnBackClick(object? sender, RoutedEventArgs e) => ShowStep(_step - 1);

    private void OnNextClick(object? sender, RoutedEventArgs e)
    {
        LinuxPlatformCapabilities.Refresh();
        UpdateBackendsSummary();
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
        UpdateBackendsSummary();

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
            var attempt = await injector.InjectWithDetailsAsync(TestPhrase, "auto", 0).ConfigureAwait(true);
            TestResultText.Text = attempt.Result.Outcome switch
            {
                echo.Abstractions.Platform.TextInjectionOutcome.AutoPasted =>
                    $"Успех — бэкенд «{attempt.BackendName}» вставил текст автоматически.",
                echo.Abstractions.Platform.TextInjectionOutcome.ClipboardOnly =>
                    $"Частично — текст скопирован ({attempt.BackendName}). Нажмите Ctrl+V в целевом поле.",
                _ => $"Не удалось: {attempt.Result.Message ?? "неизвестная ошибка"}",
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
