using System.Diagnostics;
using System.Text;

namespace echo.Platform.Linux;

public sealed record LinuxInstallResult(bool Succeeded, string Message);

public static class LinuxDependencyInstaller
{
    public static async Task<LinuxInstallResult> TryInstallAsync(
        IReadOnlyList<LinuxDependency> dependencies,
        CancellationToken cancellationToken = default)
    {
        if (dependencies.Count == 0)
        {
            return new LinuxInstallResult(true, "Все компоненты уже установлены.");
        }

        if (LinuxCommandHelper.IsFlatpakSandbox())
        {
            return new LinuxInstallResult(
                false,
                "В Flatpak установите зависимости через пакетный менеджер системы или обновите flatpak-пакет Echo.");
        }

        var packageManager = LinuxPackageManagerDetector.Detect();
        if (packageManager == LinuxPackageManager.Unknown)
        {
            return new LinuxInstallResult(false, "Не найден поддерживаемый пакетный менеджер (apt, dnf или pacman).");
        }

        if (!LinuxPackageManagerDetector.CanElevateInstall())
        {
            return new LinuxInstallResult(false, "Для установки нужен pkexec (polkit).");
        }

        var packages = dependencies
            .Select(dependency => dependency.GetPackageName(packageManager))
            .Where(package => !string.IsNullOrWhiteSpace(package))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (packages.Length == 0)
        {
            return new LinuxInstallResult(false, "Не удалось сопоставить пакеты для вашего дистрибутива.");
        }

        var shellCommand = packageManager switch
        {
            LinuxPackageManager.Apt =>
                $"export DEBIAN_FRONTEND=noninteractive; apt-get update -qq && apt-get install -y {JoinPackages(packages)}",
            LinuxPackageManager.Dnf =>
                $"dnf install -y {JoinPackages(packages)}",
            LinuxPackageManager.Pacman =>
                $"pacman -Sy --noconfirm {JoinPackages(packages)}",
            _ => string.Empty,
        };

        if (string.IsNullOrWhiteSpace(shellCommand))
        {
            return new LinuxInstallResult(false, "Установка не поддерживается на этом дистрибутиве.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "pkexec",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/bin/sh");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(shellCommand);

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Не удалось запустить pkexec.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var output = new StringBuilder();
            output.Append(await stdoutTask.ConfigureAwait(false));
            var error = await stderrTask.ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(error))
            {
                if (output.Length > 0)
                {
                    output.AppendLine();
                }

                output.Append(error);
            }

            LinuxPlatformCapabilities.Refresh();
            var remaining = LinuxDependencyCatalog.GetMissing();
            if (process.ExitCode == 0 && remaining.Count == 0)
            {
                return new LinuxInstallResult(true, "Компоненты установлены.");
            }

            if (process.ExitCode == 0 && remaining.Count > 0)
            {
                return new LinuxInstallResult(
                    false,
                    $"Установка завершилась, но не хватает: {string.Join(", ", remaining.Select(item => item.DisplayName))}.");
            }

            var details = output.ToString().Trim();
            return new LinuxInstallResult(
                false,
                string.IsNullOrWhiteSpace(details)
                    ? $"Установка завершилась с кодом {process.ExitCode}."
                    : details);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new LinuxInstallResult(false, ex.Message);
        }
    }

    private static string JoinPackages(IEnumerable<string> packages) => string.Join(' ', packages);
}
