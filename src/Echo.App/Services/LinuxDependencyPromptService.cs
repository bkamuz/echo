using System.Text.Json;
using Avalonia.Controls;
using echo.App.ViewModels;
using echo.App.Views;
using echo.Core;
using echo.Platform.Linux;
using echo.Platform.Linux.Injection;

namespace echo.App.Services;

public sealed class LinuxDependencyPromptService
{
    private const string SkipPromptKey = "linux_skip_dependency_prompt";
    private const string SetupCompleteKey = "linux_setup_complete";

    private readonly ConfigStore _configStore;
    private readonly AppStatusViewModel _status;
    private readonly DictationCoordinator _coordinator;

    public LinuxDependencyPromptService(
        ConfigStore configStore,
        AppStatusViewModel status,
        DictationCoordinator coordinator)
    {
        _configStore = configStore;
        _status = status;
        _coordinator = coordinator;
    }

    public async Task TryPromptAsync(Window owner)
    {
        if (!OperatingSystem.IsLinux() || LinuxSession.Type == LinuxSessionType.Unknown)
        {
            return;
        }

        LinuxPlatformCapabilities.Refresh();

        var config = _configStore.Load();
        var missing = LinuxPlatformCapabilities.MissingDependencies;
        var needsInputGroup = !LinuxPlatformCapabilities.SupportsGlobalHotkey;
        var setupComplete = IsSetupComplete(config);
        var dismissed = IsPromptDismissed(config);

        if (dismissed && setupComplete && missing.Count == 0 && !needsInputGroup)
        {
            ApplyHotkeyStatus();
            return;
        }

        if (dismissed && missing.Count == 0 && !needsInputGroup && LinuxInjectionChain.HasAutoInjectionBackend)
        {
            ApplyHotkeyStatus();
            return;
        }

        var dialog = new LinuxDependenciesDialog(
            missing,
            LinuxPlatformCapabilities.CanAutoInstall,
            needsInputGroup);
        _ = await dialog.ShowDialog<bool>(owner).ConfigureAwait(true);

        if (dialog.SkipPermanently)
        {
            config.Extra[SkipPromptKey] = JsonSerializer.SerializeToElement(true);
        }

        if (dialog.SetupCompleted || dialog.InstallAttempted)
        {
            config.Extra[SetupCompleteKey] = JsonSerializer.SerializeToElement(true);
        }

        _configStore.Save(config);
        LinuxPlatformCapabilities.Refresh();
        _coordinator.RestartHotkey();
        ApplyHotkeyStatus();
    }

    private void ApplyHotkeyStatus()
    {
        string? platformWarning = null;
        if (!LinuxPlatformCapabilities.SupportsGlobalHotkey
            || LinuxPlatformCapabilities.MissingDependencies.Count > 0)
        {
            platformWarning = LinuxPlatformCapabilities.StartupWarning;
        }

        _status.SetPlatformWarning(platformWarning);
    }

    private static bool IsPromptDismissed(AppConfig config) =>
        config.Extra.TryGetValue(SkipPromptKey, out var value)
        && value.ValueKind == JsonValueKind.True;

    private static bool IsSetupComplete(AppConfig config) =>
        config.Extra.TryGetValue(SetupCompleteKey, out var value)
        && value.ValueKind == JsonValueKind.True;
}
