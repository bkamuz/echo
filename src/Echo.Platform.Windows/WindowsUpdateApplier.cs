using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text;
using echo.Abstractions.Core;
using echo.Abstractions.Platform;
using Microsoft.Extensions.Logging;

namespace echo.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsUpdateApplier : IUpdateApplier
{
    private readonly HttpClient _http;
    private readonly ILogger<WindowsUpdateApplier> _logger;

    public WindowsUpdateApplier(HttpClient http, ILogger<WindowsUpdateApplier> logger)
    {
        _http = http;
        _logger = logger;
    }

    public bool IsSupported => OperatingSystem.IsWindows();

    public async Task ApplyAndRestartAsync(
        UpdateInfo update,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException("Windows in-app updates are only supported on Windows.");
        }

        if (!update.DownloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Update download URL must use HTTPS.");
        }

        var installDir = Path.GetDirectoryName(ApplicationLauncher.ResolveExecutablePath())
            ?? throw new InvalidOperationException("Could not resolve application install directory.");

        var workDir = Path.Combine(Path.GetTempPath(), "echo-update", update.Version.ToString());
        Directory.CreateDirectory(workDir);

        var zipPath = Path.Combine(workDir, "update.zip");
        var stagingDir = Path.Combine(workDir, "staging");
        var scriptPath = Path.Combine(workDir, "update.ps1");

        try
        {
            progress?.Report(ProgressMessages.DownloadingUpdate());
            await DownloadFileAsync(update.DownloadUrl, zipPath, cancellationToken).ConfigureAwait(false);

            progress?.Report(ProgressMessages.PreparingUpdate());
            if (Directory.Exists(stagingDir))
            {
                Directory.Delete(stagingDir, recursive: true);
            }

            Directory.CreateDirectory(stagingDir);
            ZipFile.ExtractToDirectory(zipPath, stagingDir, overwriteFiles: true);

            var updaterScript = BuildUpdaterScript(
                Environment.ProcessId,
                installDir,
                stagingDir);
            await File.WriteAllTextAsync(scriptPath, updaterScript, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

            progress?.Report(ProgressMessages.InstallingUpdate());
            LaunchUpdater(scriptPath);
        }
        catch
        {
            TryDeleteDirectory(workDir);
            throw;
        }
    }

    private async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = File.Create(destinationPath);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static void LaunchUpdater(string scriptPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Failed to start update script.");
        }
    }

    internal static string BuildUpdaterScript(int processId, string installDir, string stagingDir)
    {
        var exeName = "Echo.App.exe";
        var minimizedArgument = ApplicationLauncher.MinimizedArgument;

        return $$"""
            $ErrorActionPreference = 'Stop'
            $targetPid = {{processId}}
            $installDir = '{{EscapePowerShellSingleQuoted(installDir)}}'
            $stagingDir = '{{EscapePowerShellSingleQuoted(stagingDir)}}'
            $exeName = '{{exeName}}'
            $minimizedArgument = '{{minimizedArgument}}'

            try {
                $proc = Get-Process -Id $targetPid -ErrorAction SilentlyContinue
                if ($proc) {
                    Wait-Process -Id $targetPid -Timeout 120
                }
            } catch {}

            Copy-Item -Path (Join-Path $stagingDir $exeName) -Destination (Join-Path $installDir $exeName) -Force

            $directMlSrc = Join-Path $stagingDir 'directml'
            $directMlDst = Join-Path $installDir 'directml'
            if (Test-Path $directMlSrc) {
                if (Test-Path $directMlDst) {
                    Remove-Item $directMlDst -Recurse -Force
                }
                Copy-Item -Path $directMlSrc -Destination $directMlDst -Recurse -Force
            }

            Start-Process -FilePath (Join-Path $installDir $exeName) -ArgumentList $minimizedArgument
            """;
    }

    private static string EscapePowerShellSingleQuoted(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to clean up update work directory: {Path}", path);
        }
    }
}
