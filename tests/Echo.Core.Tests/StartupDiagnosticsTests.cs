using echo.Abstractions.Core;
using echo.Core.Diagnostics;

namespace echo.Core.Tests;

public class StartupDiagnosticsTests
{
    [Fact]
    public void WriteMilestone_FlushesToEchoLog()
    {
        var tempRoot = CreateTempConfigRoot(out var previousAppData, out var previousXdg);

        try
        {
            var marker = Guid.NewGuid().ToString("N");
            StartupDiagnostics.WriteMilestone($"marker={marker}");

            var log = File.ReadAllText(AppPaths.LogPath);
            Assert.Contains(marker, log);
            Assert.Contains("[Info] Startup:", log);
        }
        finally
        {
            RestoreConfigRoot(tempRoot, previousAppData, previousXdg);
        }
    }

    [Fact]
    public void WriteStartupContext_WritesProcessPathAndVersion()
    {
        var tempRoot = CreateTempConfigRoot(out var previousAppData, out var previousXdg);

        try
        {
            StartupDiagnostics.WriteStartupContext();

            var log = File.ReadAllText(AppPaths.LogPath);
            Assert.Contains("Process path=", log);
            Assert.Contains("version=", log);
        }
        finally
        {
            RestoreConfigRoot(tempRoot, previousAppData, previousXdg);
        }
    }

    [Fact]
    public void WriteFatal_AppendsToEchoLog()
    {
        var tempRoot = CreateTempConfigRoot(out var previousAppData, out var previousXdg);

        try
        {
            var marker = Guid.NewGuid().ToString("N");
            StartupDiagnostics.WriteFatal($"marker={marker}", new InvalidOperationException("test failure"));

            var log = File.ReadAllText(AppPaths.LogPath);
            Assert.Contains(marker, log);
            Assert.Contains("test failure", log);
        }
        finally
        {
            RestoreConfigRoot(tempRoot, previousAppData, previousXdg);
        }
    }

    private static string CreateTempConfigRoot(out string? previousAppData, out string? previousXdg)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "echo-startup-diag-" + Guid.NewGuid().ToString("N"));
        previousAppData = Environment.GetEnvironmentVariable("APPDATA");
        previousXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Environment.SetEnvironmentVariable("APPDATA", tempRoot);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", tempRoot);
        return tempRoot;
    }

    private static void RestoreConfigRoot(string tempRoot, string? previousAppData, string? previousXdg)
    {
        Environment.SetEnvironmentVariable("APPDATA", previousAppData);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousXdg);
        try
        {
            Directory.Delete(tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
