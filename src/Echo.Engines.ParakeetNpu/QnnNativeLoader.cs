using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace echo.Engines.ParakeetNpu;

/// <summary>
/// Windows QNN native library search-path setup and explicit loads with actionable errors.
/// </summary>
internal static class QnnNativeLoader
{
    private const uint LoadWithAlteredSearchPath = 0x00000008;

    private static readonly object Gate = new();
    private static string? _preparedDir;

    public static void PrepareSearchPath(string runtimeDir, ILogger? logger = null)
    {
        var dir = Path.GetFullPath(runtimeDir);
        lock (Gate)
        {
            if (!Directory.Exists(dir))
            {
                return;
            }

            if (string.Equals(_preparedDir, dir, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            PrependPathEntry(dir);
            EnsureAdspLibraryPath(dir, logger);

            if (OperatingSystem.IsWindows())
            {
                EnsureDllDirectory(dir, logger);
            }

            var ortDll = Path.Combine(dir, "onnxruntime.dll");
            if (QnnRuntimePaths.IsFilePresent(ortDll))
            {
                Environment.SetEnvironmentVariable("ORT_DYLIB_PATH", ortDll);
            }

            _preparedDir = dir;
            logger?.LogDebug("Prepared QNN native search path for {Dir}", dir);
        }
    }

    public static nint LoadLibrary(string libraryPath, ILogger? logger = null)
    {
        var fullPath = Path.GetFullPath(libraryPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Native library not found: {fullPath}", fullPath);
        }

        logger?.LogInformation(
            "Loading QNN native library {FileName} from {Path}",
            Path.GetFileName(fullPath),
            fullPath);

        if (OperatingSystem.IsWindows())
        {
            var handle = LoadLibraryExW(fullPath, nint.Zero, LoadWithAlteredSearchPath);
            if (handle == nint.Zero)
            {
                throw CreateLoadFailure(fullPath, Marshal.GetLastWin32Error());
            }

            return handle;
        }

        try
        {
            return NativeLibrary.Load(fullPath);
        }
        catch (DllNotFoundException ex)
        {
            throw WrapLoadFailure(fullPath, ex);
        }
    }

    internal static string PrependPathEntry(string? currentPath, string entry)
    {
        var normalizedEntry = Path.GetFullPath(entry);
        var segments = (currentPath ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => string.Equals(segment, normalizedEntry, StringComparison.OrdinalIgnoreCase)))
        {
            return currentPath ?? string.Empty;
        }

        return string.IsNullOrEmpty(currentPath)
            ? normalizedEntry
            : normalizedEntry + Path.PathSeparator + currentPath;
    }

    internal static string PrependAdspLibraryPath(string? currentPath, string runtimeDir)
    {
        var normalizedDir = Path.GetFullPath(runtimeDir);
        var segments = (currentPath ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => string.Equals(segment, normalizedDir, StringComparison.OrdinalIgnoreCase)))
        {
            return currentPath ?? string.Empty;
        }

        return string.IsNullOrEmpty(currentPath)
            ? normalizedDir
            : normalizedDir + ";" + currentPath;
    }

    private static void PrependPathEntry(string dir)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        Environment.SetEnvironmentVariable("PATH", PrependPathEntry(path, dir));
    }

    private static void EnsureAdspLibraryPath(string runtimeDir, ILogger? logger)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var current = Environment.GetEnvironmentVariable("ADSP_LIBRARY_PATH");
        var updated = PrependAdspLibraryPath(current, runtimeDir);
        if (!string.Equals(current, updated, StringComparison.Ordinal))
        {
            Environment.SetEnvironmentVariable("ADSP_LIBRARY_PATH", updated);
            logger?.LogInformation(
                "Set ADSP_LIBRARY_PATH for QNN HTP skel discovery (runtime={RuntimeDir})",
                runtimeDir);
        }
    }

    private static bool _defaultDllDirectoriesConfigured;
    private static readonly HashSet<string> AddedDllDirectories = new(StringComparer.OrdinalIgnoreCase);

    private static void EnsureDllDirectory(string runtimeDir, ILogger? logger)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!_defaultDllDirectoriesConfigured)
        {
            const uint loadLibrarySearchDefaultDirs = 0x00001000;
            const uint loadLibrarySearchUserDirs = 0x00000400;
            if (!SetDefaultDllDirectories(loadLibrarySearchDefaultDirs | loadLibrarySearchUserDirs))
            {
                logger?.LogWarning(
                    "SetDefaultDllDirectories failed for QNN runtime (win32={Win32Error}); continuing with altered search-path loads.",
                    Marshal.GetLastWin32Error());
            }
            else
            {
                _defaultDllDirectoriesConfigured = true;
            }
        }

        if (AddedDllDirectories.Add(runtimeDir))
        {
            var cookie = AddDllDirectory(runtimeDir);
            if (cookie == nint.Zero)
            {
                logger?.LogWarning(
                    "AddDllDirectory failed for QNN runtime {RuntimeDir} (win32={Win32Error}).",
                    runtimeDir,
                    Marshal.GetLastWin32Error());
            }
            else
            {
                logger?.LogDebug("Added QNN runtime directory to DLL search path: {RuntimeDir}", runtimeDir);
            }
        }
    }

    private static DllNotFoundException CreateLoadFailure(string fullPath, int win32Error)
    {
        var fileName = Path.GetFileName(fullPath);
        var detail = win32Error == 0
            ? "The module or one of its dependencies could not be found."
            : new Win32Exception(win32Error).Message;
        return new DllNotFoundException(
            $"Failed to load native library '{fileName}' from {fullPath}. {detail} (win32={win32Error})");
    }

    private static DllNotFoundException WrapLoadFailure(string fullPath, Exception inner) =>
        new(
            $"Failed to load native library '{Path.GetFileName(fullPath)}' from {fullPath}. {inner.Message}",
            inner);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryExW")]
    private static extern nint LoadLibraryExW(string lpLibFileName, nint hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint AddDllDirectory(string lpPathName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDefaultDllDirectories(uint DirectoryFlags);
}
