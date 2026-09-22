using echo.Abstractions.Core;
using echo.Core.Diagnostics;

namespace echo.Core.Tests;

public class SherpaWorkerDiagnosticsTests
{
    [Fact]
    public void WriteMilestone_FlushesToDedicatedWorkerLog()
    {
        var tempRoot = CreateTempConfigRoot(out var previousAppData, out var previousXdg);

        try
        {
            var marker = Guid.NewGuid().ToString("N");
            SherpaWorkerDiagnostics.WriteMilestone($"marker={marker}");

            var log = File.ReadAllText(AppPaths.SherpaWorkerLogPath);
            Assert.Contains(marker, log);
            Assert.Contains("[Info] Worker:", log);
            if (File.Exists(AppPaths.LogPath))
            {
                Assert.DoesNotContain(marker, File.ReadAllText(AppPaths.LogPath));
            }
        }
        finally
        {
            RestoreConfigRoot(tempRoot, previousAppData, previousXdg);
        }
    }

    [Fact]
    public void TryReadTail_ReturnsPlaceholderWhenMissing()
    {
        var tempRoot = CreateTempConfigRoot(out var previousAppData, out var previousXdg);

        try
        {
            var tail = SherpaWorkerDiagnostics.TryReadTail();
            Assert.Contains("worker log not created", tail);
        }
        finally
        {
            RestoreConfigRoot(tempRoot, previousAppData, previousXdg);
        }
    }

    private static string CreateTempConfigRoot(out string? previousAppData, out string? previousXdg)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "echo-worker-diag-" + Guid.NewGuid().ToString("N"));
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
