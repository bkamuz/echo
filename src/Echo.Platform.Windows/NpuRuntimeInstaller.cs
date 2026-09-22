using System.Runtime.InteropServices;
using echo.Abstractions.Core;
using Microsoft.Extensions.Logging;

namespace echo.Platform.Windows;

/// <summary>
/// Prepares QNN-capable Sherpa/ORT natives for Windows ARM64 (Snapdragon NPU).
/// Runtime assets are not yet published; this mirrors the DirectML installer pattern.
/// </summary>
public sealed class NpuRuntimeInstaller
{
    private const string MaintainerRepo = "bkamuz/echo";
    private const string DefaultVersion = "1.13.4";

    private readonly HttpClient _http;
    private readonly ILogger<NpuRuntimeInstaller> _logger;

    public NpuRuntimeInstaller(HttpClient http, ILogger<NpuRuntimeInstaller> logger)
    {
        _http = http;
        _logger = logger;
    }

    public bool IsInstalled => NpuPaths.IsInstalled;

    public async Task EnsureInstalledAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()
            || RuntimeInformation.ProcessArchitecture is not Architecture.Arm64 and not Architecture.Arm)
        {
            throw new PlatformNotSupportedException(
                "QNN runtime is only supported on Windows ARM64 (Snapdragon).");
        }

        if (IsInstalled)
        {
            NpuPaths.PrepareNativeSearchPath();
            return;
        }

        progress?.Report(ProgressMessages.Downloading("QNN"));
        var version = ResolveRuntimeVersion();
        var tag = $"qnn-runtime-{version}";
        var dest = NpuPaths.UserDir;
        Directory.CreateDirectory(dest);

        var files = new[]
        {
            "sherpa-onnx-c-api.dll",
            "onnxruntime.dll",
            "onnxruntime_providers_qnn.dll",
            "QnnHtp.dll",
            "QnnSystem.dll",
        };

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(ProgressMessages.Downloading(file));
            var url = $"https://github.com/{MaintainerRepo}/releases/download/{tag}/{file}";
            var target = Path.Combine(dest, file);
            var tmp = target + ".tmp";

            _logger.LogInformation("Downloading QNN asset {File} from {Url}", file, url);
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException(
                    $"QNN runtime release '{tag}' is not published yet. See docs/npu-qnn-win-arm64.md.");
            }

            response.EnsureSuccessStatusCode();
            await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var fileStream = File.Create(tmp))
            {
                await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tmp, target, overwrite: true);
        }

        NpuPaths.TryMirrorBesideApp(dest);
        NpuPaths.PrepareNativeSearchPath();

        if (!NpuPaths.IsInstalled)
        {
            throw new InvalidOperationException(
                "QNN runtime downloaded but failed verification. Try again or use CPU.");
        }

        progress?.Report(ProgressMessages.Done("QNN"));
        _logger.LogInformation("QNN runtime installed to {Dir}", dest);
    }

    private static string ResolveRuntimeVersion()
    {
        var pinned = FindPinnedVersion();
        return string.IsNullOrWhiteSpace(pinned) ? DefaultVersion : pinned.Trim();
    }

    private static string? FindPinnedVersion()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "qnn-sherpa-version"),
            Path.Combine(AppContext.BaseDirectory, ".github", "qnn-sherpa-version"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return File.ReadAllText(path).Trim();
            }
        }

        return null;
    }
}

internal static class NpuPaths
{
    public static string UserDir => AppPaths.NpuDir;

    public static string? ResolveDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "qnn"),
            UserDir,
        };

        foreach (var dir in candidates)
        {
            if (File.Exists(Path.Combine(dir, "sherpa-onnx-c-api.dll"))
                && OrtQnnExport.IsPresent(dir))
            {
                return dir;
            }
        }

        return null;
    }

    public static bool IsInstalled => ResolveDirectory() is not null;

    public static void TryMirrorBesideApp(string sourceDir)
    {
        var appLocal = Path.Combine(AppContext.BaseDirectory, "qnn");
        if (string.Equals(
                Path.GetFullPath(sourceDir).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(appLocal).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(appLocal);
            foreach (var file in Directory.EnumerateFiles(sourceDir, "*.dll"))
            {
                var name = Path.GetFileName(file);
                File.Copy(file, Path.Combine(appLocal, name), overwrite: true);
            }
        }
        catch
        {
            // Program Files installs may be read-only; PrepareNativeSearchPath covers AppData.
        }
    }

    public static void PrepareNativeSearchPath()
    {
        var dir = ResolveDirectory();
        if (dir is null)
        {
            return;
        }

        try
        {
            NativeLibrary.Load(Path.Combine(dir, "onnxruntime.dll"));
        }
        catch
        {
            // Best-effort preload so subsequent Sherpa loads prefer this ORT build.
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(p => string.Equals(p, dir, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + path);
    }
}
