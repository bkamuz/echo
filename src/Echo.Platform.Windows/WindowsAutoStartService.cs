using echo.Abstractions.Platform;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace echo.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsAutoStartService : IAutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Echo";

    public bool IsSupported => true;

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Failed to open Windows Run registry key.");

        if (enabled)
        {
            var command = ApplicationLauncher.FormatLaunchCommand(ApplicationLauncher.ResolveExecutablePath());
            key.SetValue(ValueName, command);
            return;
        }

        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
