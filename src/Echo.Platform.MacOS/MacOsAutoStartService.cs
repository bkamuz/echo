using System.Diagnostics;
using System.Text;
using System.Xml;
using echo.Abstractions.Platform;

namespace echo.Platform.MacOS;

public sealed class MacOsAutoStartService : IAutoStartService
{
    private const string LaunchAgentLabel = "com.echo.app";

    public bool IsSupported => true;

    public bool IsEnabled => File.Exists(LaunchAgentPath);

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            Directory.CreateDirectory(LaunchAgentsDirectory);
            var executablePath = ApplicationLauncher.ResolveExecutablePath();
            File.WriteAllText(LaunchAgentPath, BuildLaunchAgentPlist(executablePath), Encoding.UTF8);
            RunLaunchCtl("bootstrap", GuiDomain, LaunchAgentPath);
            return;
        }

        if (!File.Exists(LaunchAgentPath))
        {
            return;
        }

        RunLaunchCtl("bootout", GuiDomain, LaunchAgentPath);
        File.Delete(LaunchAgentPath);
    }

    private static string LaunchAgentsDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "LaunchAgents");

    private static string LaunchAgentPath => Path.Combine(LaunchAgentsDirectory, $"{LaunchAgentLabel}.plist");

    private static string GuiDomain => $"gui/{GetUserId()}";

    private static string BuildLaunchAgentPlist(string executablePath)
    {
        var escapedPath = XmlEscape(executablePath);
        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                  <key>Label</key>
                  <string>{LaunchAgentLabel}</string>
                  <key>ProgramArguments</key>
                  <array>
                    <string>{escapedPath}</string>
                    <string>{ApplicationLauncher.MinimizedArgument}</string>
                  </array>
                  <key>RunAtLoad</key>
                  <true/>
                </dict>
                </plist>
                """;
    }

    private static string XmlEscape(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);

    private static int GetUserId()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "/usr/bin/id",
            Arguments = "-u",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Failed to resolve macOS user id.");

        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return int.Parse(output, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void RunLaunchCtl(string verb, string domain, string plistPath)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/launchctl",
            Arguments = $"{verb} {domain} \"{plistPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        process?.WaitForExit();
    }
}
