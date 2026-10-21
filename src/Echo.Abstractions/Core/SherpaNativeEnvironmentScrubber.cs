namespace echo.Abstractions.Core;

/// <summary>
/// Sanitizes ORT/Sherpa-related environment before native init on Windows.
/// Parakeet QNN downloads a separate onnxruntime.dll into %APPDATA%\Echo\qnn\ and
/// prepends that folder to PATH / ORT_DYLIB_PATH; loading Sherpa against the wrong
/// ORT build can native-abort the process.
/// </summary>
public static class SherpaNativeEnvironmentScrubber
{
    public static void PrepareForLoad()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ClearConflictingOrtDylibPath(Environment.GetEnvironmentVariable("ORT_DYLIB_PATH"));
        RemoveDirectoryFromProcessPath(AppPaths.NpuDir);
        RemoveDirectoryFromProcessPath(AppPaths.DirectMlDir);
    }

    public static void PrepareForLoad(IDictionary<string, string?> environment)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ClearConflictingOrtDylibPath(environment);
        RemoveDirectoryFromPath(environment, AppPaths.NpuDir);
        RemoveDirectoryFromPath(environment, AppPaths.DirectMlDir);
    }

    /// <summary>Drops PATH entries under %APPDATA%\Echo (qnn, qnn-whisper, directml runtimes).</summary>
    public static string RemoveEchoRuntimeDirectories(string path)
    {
        var baseDir = Path.GetFullPath(AppPaths.BaseDir).TrimEnd('\\', '/');
        var kept = path
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(entry =>
            {
                var trimmed = entry.Trim().Trim('"').TrimEnd('\\', '/');
                return !(trimmed.Equals(baseDir, StringComparison.OrdinalIgnoreCase)
                         || trimmed.StartsWith(baseDir + "\\", StringComparison.OrdinalIgnoreCase)
                         || trimmed.StartsWith(baseDir + "/", StringComparison.OrdinalIgnoreCase));
            });
        return string.Join(';', kept);
    }

    /// <summary>Main-process startup: drop runtime folders/variables inherited from a parent Echo (e.g. updater relaunch).</summary>
    public static bool ScrubInheritedEnvironmentAtStartup()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var changed = false;
        foreach (var name in new[] { "ORT_DYLIB_PATH", "ADSP_LIBRARY_PATH" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value) && value.Contains(Path.GetFullPath(AppPaths.BaseDir), StringComparison.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable(name, null);
                changed = true;
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var filtered = RemoveEchoRuntimeDirectories(path);
        var normalized = string.Join(';', path.Split(';', StringSplitOptions.RemoveEmptyEntries));
        if (!string.Equals(filtered, normalized, StringComparison.Ordinal))
        {
            Environment.SetEnvironmentVariable("PATH", filtered);
            changed = true;
        }

        return changed;
    }

    public static string DescribeSnapshot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "non-windows";
        }

        var ortDylib = Environment.GetEnvironmentVariable("ORT_DYLIB_PATH");
        var pathHasQnn = PathContainsDirectory(AppPaths.NpuDir);
        var pathHasDirectMl = PathContainsDirectory(AppPaths.DirectMlDir);
        var ortPart = string.IsNullOrWhiteSpace(ortDylib) ? "unset" : ortDylib;
        return $"ORT_DYLIB_PATH={ortPart}; PATH_has_qnn={pathHasQnn}; PATH_has_directml={pathHasDirectMl}";
    }

    private static void ClearConflictingOrtDylibPath(string? ortDylib)
    {
        if (string.IsNullOrWhiteSpace(ortDylib))
        {
            return;
        }

        var ortDir = Path.GetDirectoryName(ortDylib);
        if (ortDir is null || IsUnderEchoDataDir(ortDir))
        {
            Environment.SetEnvironmentVariable("ORT_DYLIB_PATH", null);
        }
    }

    private static void ClearConflictingOrtDylibPath(IDictionary<string, string?> environment)
    {
        if (!environment.TryGetValue("ORT_DYLIB_PATH", out var ortDylib)
            || string.IsNullOrWhiteSpace(ortDylib))
        {
            environment.Remove("ORT_DYLIB_PATH");
            return;
        }

        var ortDir = Path.GetDirectoryName(ortDylib);
        if (ortDir is null || IsUnderEchoDataDir(ortDir))
        {
            environment.Remove("ORT_DYLIB_PATH");
        }
    }

    private static void RemoveDirectoryFromProcessPath(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var fullDir = Path.GetFullPath(directory);
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var filtered = FilterPathEntries(path, fullDir);
        Environment.SetEnvironmentVariable("PATH", filtered);
    }

    private static void RemoveDirectoryFromPath(IDictionary<string, string?> environment, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var fullDir = Path.GetFullPath(directory);
        if (!environment.TryGetValue("PATH", out var path) || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        environment["PATH"] = FilterPathEntries(path, fullDir);
    }

    private static string FilterPathEntries(string path, string fullDirToRemove)
    {
        var filtered = path
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(entry =>
            {
                try
                {
                    return !string.Equals(Path.GetFullPath(entry), fullDirToRemove, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return true;
                }
            });

        return string.Join(Path.PathSeparator, filtered);
    }

    private static bool PathContainsDirectory(string directory)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fullDir = Path.GetFullPath(directory);
        return path
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(entry =>
            {
                try
                {
                    return string.Equals(Path.GetFullPath(entry), fullDir, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            });
    }

    private static bool IsUnderEchoDataDir(string directory)
    {
        try
        {
            var full = Path.GetFullPath(directory);
            var echoBase = Path.GetFullPath(AppPaths.BaseDir);
            return full.StartsWith(echoBase, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
