using echo.Abstractions.Core;
using echo.Core.Diagnostics;

namespace echo.Core.Tests;

public class StartupDiagnosticsTests
{
    [Fact]
    public void WriteFatal_AppendsToEchoLog()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "echo-startup-diag-" + Guid.NewGuid().ToString("N"));
        var previousAppData = Environment.GetEnvironmentVariable("APPDATA");
        var previousXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Environment.SetEnvironmentVariable("APPDATA", tempRoot);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", tempRoot);

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
}
