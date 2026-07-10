using System.Reflection;
using echo.Abstractions.Core;

namespace echo.Core.Update;

public static class UpdateEnvironment
{
    public const string GitHubOwner = "bkamuz";
    public const string GitHubRepo = "echo";
    public const string WindowsPortableAssetSuffix = "-win-x64-portable.zip";

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static bool IsPublishedBuild
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            var path = Environment.ProcessPath;
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

        if (version <= CurrentVersion)
        {
            return null;
        }

        return new UpdateInfo
        {
            Version = version,
            DownloadUrl = config.PendingUpdateDownloadUrl,
            ReleaseNotesUrl = config.PendingUpdateReleaseNotesUrl,
        };
    }
}
