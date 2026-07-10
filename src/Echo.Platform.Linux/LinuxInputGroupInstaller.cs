using System.Diagnostics;

namespace echo.Platform.Linux;

public static class LinuxInputGroupInstaller
{
    public static bool IsCurrentUserInInputGroup() =>
        LinuxHotkeySetup.HasActiveInputGroupSession();

    public static async Task<LinuxInstallResult> TryAddCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var user = Environment.UserName;
        if (string.IsNullOrWhiteSpace(user))
        {
            return new LinuxInstallResult(false, "Не удалось определить имя пользователя.");
        }

        if (LinuxHotkeySetup.GetAccessState() == LinuxInputAccessState.Granted)
        {
            return new LinuxInstallResult(true, "Группа input уже настроена.");
        }

        if (LinuxHotkeySetup.GetAccessState() == LinuxInputAccessState.PendingRelogin)
        {
            return new LinuxInstallResult(false, LinuxHotkeySetup.GetSetupMessage());
        }

        if (!LinuxPackageManagerDetector.CanElevateInstall())
        {
            return new LinuxInstallResult(
                false,
                $"Выполните вручную: sudo usermod -aG input {user} и перезайдите в сессию.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "pkexec",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("usermod");
        startInfo.ArgumentList.Add("-aG");
        startInfo.ArgumentList.Add("input");
        startInfo.ArgumentList.Add(user);

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Не удалось запустить pkexec.");

            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var details = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim();
                return new LinuxInstallResult(
                    false,
                    string.IsNullOrWhiteSpace(details)
                        ? $"usermod завершился с кодом {process.ExitCode}."
                        : details);
            }

            LinuxPlatformCapabilities.Refresh();
            return new LinuxInstallResult(
                true,
                $"Пользователь {user} добавлен в группу input. Перезайдите в сессию и перезапустите Echo.");
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

    private static string RunCommand(string fileName, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException($"Failed to start {fileName}.");

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }
}
