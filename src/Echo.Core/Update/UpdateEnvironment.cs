using System.Reflection;
using echo.Abstractions.Core;
using echo.Abstractions.Platform;

namespace echo.Core.Update;

public static class UpdateEnvironment
{
    public const string GitHubOwner = "bkamuz";
    public const string GitHubRepo = "echo";
    public const string WindowsPortableAssetSuffix = "-win-x64-portable.zip";

    public static string UpdateManifestUrl =>
        $"https://raw.githubusercontent.com/{GitHubOwner}/{GitHubRepo}/main/latest.json";

    public static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version
        ?? Assembly.GetExecutingAssembly().GetName().Version
        ?? new Version(0, 0, 0);

    public static string DisplayVersion
    {
        get
        {
            var version = NormalizeVersion(CurrentVersion);
            return $"v{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public static Version NormalizeVersion(Version version) =>
        new(version.Major, version.Minor, version.Build);

    public static bool IsNewerVersion(Version available, Version current) =>
        NormalizeVersion(available) > NormalizeVersion(current);

    public static bool IsPublishedBuild
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            var path = ResolveProcessPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (!path.EndsWith("Echo.App.exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool ShouldQueryRemote(DateTimeOffset? lastCheckUtc) =>
        lastCheckUtc is null || DateTimeOffset.UtcNow - lastCheckUtc.Value >= TimeSpan.FromHours(24);

    public static UpdateInfo? TryCreatePendingUpdate(AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.PendingUpdateVersion)
            || string.IsNullOrWhiteSpace(config.PendingUpdateDownloadUrl))
        {
            return null;
        }

        if (!Version.TryParse(config.PendingUpdateVersion, out var version))
        {
            return null;
        }

        if (!IsNewerVersion(version, CurrentVersion))
        {
            return null;
        }

        return new UpdateInfo
        {
            Version = NormalizeVersion(version),
            DownloadUrl = config.PendingUpdateDownloadUrl,
            ReleaseNotesUrl = config.PendingUpdateReleaseNotesUrl,
        };
    }

    private static string? ResolveProcessPath()
    {
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return Environment.ProcessPath;
        }

        try
        {
            return ApplicationLauncher.ResolveExecutablePath();
        }
        catch
        {
            return null;
        }
    }
}
