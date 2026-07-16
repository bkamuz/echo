using System.Runtime.InteropServices;
using echo.Abstractions.Core;
using Microsoft.Extensions.Logging;

namespace echo.Platform.Windows;

/// <summary>
/// Downloads DirectML Sherpa natives into the user data folder on first GPU use,
/// then mirrors them beside the app when the install directory is writable.
/// </summary>
public sealed class DirectMlRuntimeInstaller
{
    private const string MaintainerRepo = "bkamuz/echo";
    private const string DefaultVersion = "1.13.4";

    private readonly HttpClient _http;
    private readonly ILogger<DirectMlRuntimeInstaller> _logger;

    public DirectMlRuntimeInstaller(HttpClient http, ILogger<DirectMlRuntimeInstaller> logger)
    {
        _http = http;
        _logger = logger;
    }

    public bool IsInstalled => DirectMlPaths.IsInstalled;

    public async Task EnsureInstalledAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsInstalled)
        {
            DirectMlPaths.PrepareNativeSearchPath();
            return;
        }

        progress?.Report("Скачивание DirectML…");
        var version = ResolveRuntimeVersion();
        var tag = $"directml-runtime-{version}";
        var dest = AppPaths.DirectMlDir;
        Directory.CreateDirectory(dest);

        var files = new[]
        {
            "sherpa-onnx-c-api.dll",
            "onnxruntime.dll",
            "DirectML.dll",
        };

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Скачивание {file}…");
            var url =
                $"https://github.com/{MaintainerRepo}/releases/download/{tag}/{file}";
            var target = Path.Combine(dest, file);
            var tmp = target + ".tmp";

            _logger.LogInformation("Downloading DirectML asset {File} from {Url}", file, url);
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var fileStream = File.Create(tmp))
            {
                await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tmp, target, overwrite: true);
        }

        DirectMlPaths.TryMirrorBesideApp(dest);
        DirectMlPaths.PrepareNativeSearchPath();

        if (!DirectMlPaths.IsInstalled)
        {
            throw new InvalidOperationException(
                "DirectML runtime скачан, но не прошёл проверку. Попробуйте снова или используйте CPU.");
        }

        progress?.Report("Готово: DirectML");
        _logger.LogInformation("DirectML runtime installed to {Dir}", dest);
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
            Path.Combine(AppContext.BaseDirectory, "directml-sherpa-version"),
            Path.Combine(AppContext.BaseDirectory, ".github", "directml-sherpa-version"),
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

internal static class DirectMlPaths
{
    public static string? ResolveDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "directml"),
            AppPaths.DirectMlDir,
        };

        foreach (var dir in candidates)
        {
            if (File.Exists(Path.Combine(dir, "sherpa-onnx-c-api.dll"))
                && OrtDirectMlExport.IsPresent(dir))
            {
                return dir;
            }
        }

        return null;
    }

    public static bool IsInstalled => ResolveDirectory() is not null;

    public static void TryMirrorBesideApp(string sourceDir)
    {
        var appLocal = Path.Combine(AppContext.BaseDirectory, "directml");
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
