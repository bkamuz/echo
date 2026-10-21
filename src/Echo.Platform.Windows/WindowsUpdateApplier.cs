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
                stagingDir,
                AppPaths.UpdateLogPath);
            await File.WriteAllTextAsync(scriptPath, updaterScript, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

            progress?.Report(ProgressMessages.InstallingUpdate());
            _logger.LogInformation(
                "Update {Version}: staged in {Staging}, installing to {InstallDir}; updater log {Log}",
                update.Version,
                stagingDir,
                installDir,
                AppPaths.UpdateLogPath);
            LaunchUpdater(scriptPath);
            _logger.LogInformation("Update {Version}: updater started; Echo will exit and relaunch", update.Version);
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

        // The relaunched Echo inherits this environment. This process may have prepended
        // %APPDATA%\Echo\qnn / directml (their own onnxruntime.dll) to PATH and set
        // ORT_DYLIB_PATH / ADSP_LIBRARY_PATH; the new Echo's Sherpa would then bind the wrong ORT
        // and native-crash on start. A manual start never sees these, so scrub them here.
        SanitizeChildEnvironment(startInfo.Environment);

        var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Failed to start update script.");
        }
    }

    /// <summary>Removes Echo-owned native runtime folders and ORT/QNN variables from a child environment.</summary>
    internal static void SanitizeChildEnvironment(IDictionary<string, string?> environment)
    {
        environment.Remove("ORT_DYLIB_PATH");
        environment.Remove("ADSP_LIBRARY_PATH");
        if (environment.TryGetValue("PATH", out var path) && !string.IsNullOrEmpty(path))
        {
            environment["PATH"] = SherpaNativeEnvironmentScrubber.RemoveEchoRuntimeDirectories(path);
        }
    }

    internal static string BuildUpdaterScript(int processId, string installDir, string stagingDir, string logPath)
    {
        var exeName = "Echo.App.exe";
        var echoDataDir = AppPaths.BaseDir;

        return $$"""
            $ErrorActionPreference = 'Stop'
            $targetPid = {{processId}}
            $installDir = '{{EscapePowerShellSingleQuoted(installDir)}}'
            $stagingDir = '{{EscapePowerShellSingleQuoted(stagingDir)}}'
            $logPath = '{{EscapePowerShellSingleQuoted(logPath)}}'
            $echoDataDir = '{{EscapePowerShellSingleQuoted(echoDataDir)}}'
            $exeName = '{{exeName}}'
            $exePath = Join-Path $installDir $exeName

            function Log([string]$m) {
                try { Add-Content -LiteralPath $logPath -Value ("{0:yyyy-MM-dd HH:mm:ss.fff} [updater] {1}" -f (Get-Date), $m) -Encoding UTF8 } catch {}
            }

            function Copy-WithRetry([string]$src, [string]$dst, [switch]$Recurse) {
                for ($i = 1; $i -le 20; $i++) {
                    try {
                        if ($Recurse) { Copy-Item -LiteralPath $src -Destination $dst -Recurse -Force }
                        else { Copy-Item -LiteralPath $src -Destination $dst -Force }
                        return
                    } catch {
                        Log "copy attempt $i failed for ${src}: $($_.Exception.Message)"
                        if ($i -eq 20) { throw }
                        Start-Sleep -Milliseconds 500
                    }
                }
            }

            Log "start pid=$PID target=$targetPid install=$installDir staging=$stagingDir"
            try {
                if (Get-Process -Id $targetPid -ErrorAction SilentlyContinue) {
                    Wait-Process -Id $targetPid -Timeout 60 -ErrorAction SilentlyContinue
                }
                if (Get-Process -Id $targetPid -ErrorAction SilentlyContinue) {
                    Log "Echo pid $targetPid still running after 60s; stopping it"
                    Stop-Process -Id $targetPid -Force -ErrorAction SilentlyContinue
                }

                # Sherpa / QNN workers run from the same exe and keep it locked.
                Get-Process -Name 'Echo.App' -ErrorAction SilentlyContinue |
                    Where-Object { $_.Path -and ($_.Path -ieq $exePath) } |
                    ForEach-Object {
                        Log "stopping leftover Echo process pid=$($_.Id)"
                        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
                        $_.WaitForExit(10000) | Out-Null
                    }

                Copy-WithRetry (Join-Path $stagingDir $exeName) $exePath
                Log "copied $exeName"

                # Copy every other file/subfolder shipped in the zip (e.g. fe\), not just the exe.
                Get-ChildItem -LiteralPath $stagingDir -Force | Where-Object { $_.Name -ne $exeName -and $_.Name -ne 'directml' } | ForEach-Object {
                    Copy-WithRetry $_.FullName $installDir -Recurse
                    Log "copied $($_.Name)"
                }

                $directMlSrc = Join-Path $stagingDir 'directml'
                $directMlDst = Join-Path $installDir 'directml'
                if (Test-Path $directMlSrc) {
                    if (Test-Path $directMlDst) {
                        Remove-Item $directMlDst -Recurse -Force
                    }
                    Copy-WithRetry $directMlSrc $directMlDst -Recurse
                    Log "copied directml"
                }
                Log "files installed"
            } catch {
                Log "install failed: $($_.Exception.Message)"
            }

            # Clean native search environment so the new Echo cannot bind Echo's QNN/DirectML onnxruntime.dll.
            Remove-Item Env:ORT_DYLIB_PATH -ErrorAction SilentlyContinue
            Remove-Item Env:ADSP_LIBRARY_PATH -ErrorAction SilentlyContinue
            $env:PATH = (($env:PATH -split ';') | Where-Object { $_ -and -not $_.StartsWith($echoDataDir, [System.StringComparison]::OrdinalIgnoreCase) }) -join ';'

            try {
                # Relaunch visible (no minimized flag): the user just clicked "Update".
                $p = Start-Process -FilePath $exePath -WorkingDirectory $installDir -WindowStyle Normal -PassThru
                Log "relaunched $exePath pid=$($p.Id)"
            } catch {
                Log "relaunch failed: $($_.Exception.Message)"
            }
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
