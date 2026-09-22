using System.Runtime.InteropServices;
using echo.Abstractions.Core;

namespace echo.Engines;

/// <summary>
/// Sanitizes process environment before Sherpa/ORT native init on Windows ARM64.
/// Parakeet QNN downloads a separate onnxruntime.dll into %APPDATA%\Echo\qnn\ and
/// prepends that folder to PATH / ORT_DYLIB_PATH; loading Sherpa against the wrong
/// ORT build can native-abort the process.
/// </summary>
public static class SherpaNativeEnvironment
{
    public static void PrepareForLoad()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ClearConflictingOrtDylibPath();
        RemoveDirectoryFromPath(AppPaths.NpuDir);
        RemoveDirectoryFromPath(AppPaths.DirectMlDir);
    }

    public static int ResolveNumThreads()
    {
        var cores = Math.Max(1, Environment.ProcessorCount);
        if (OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            return Math.Min(cores, 4);
        }

        return cores;
    }

    private static void ClearConflictingOrtDylibPath()
    {
        var ortDylib = Environment.GetEnvironmentVariable("ORT_DYLIB_PATH");
        if (string.IsNullOrWhiteSpace(ortDylib))
        {
            return;
        }

        var ortDir = Path.GetDirectoryName(ortDylib);
        if (ortDir is null)
        {
            Environment.SetEnvironmentVariable("ORT_DYLIB_PATH", null);
            return;
        }

        if (IsUnderEchoDataDir(ortDir))
        {
            Environment.SetEnvironmentVariable("ORT_DYLIB_PATH", null);
        }
    }

    private static void RemoveDirectoryFromPath(string directory)
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

        var filtered = path
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(entry =>
            {
                try
                {
                    return !string.Equals(Path.GetFullPath(entry), fullDir, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return true;
                }
            });

        Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, filtered));
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
