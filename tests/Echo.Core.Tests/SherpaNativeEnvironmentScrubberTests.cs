using echo.Abstractions.Core;

namespace echo.Core.Tests;

public class SherpaNativeEnvironmentScrubberTests
{
    [Fact]
    public void PrepareForLoad_RemovesEchoQnnDirFromPath_OnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var originalPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var originalOrt = Environment.GetEnvironmentVariable("ORT_DYLIB_PATH");
        var qnnDir = AppPaths.NpuDir;
        Environment.SetEnvironmentVariable("PATH", qnnDir + Path.PathSeparator + originalPath);
        Environment.SetEnvironmentVariable("ORT_DYLIB_PATH", Path.Combine(qnnDir, "onnxruntime.dll"));

        try
        {
            SherpaNativeEnvironmentScrubber.PrepareForLoad();

            var updatedPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            Assert.DoesNotContain(qnnDir, updatedPath, StringComparison.OrdinalIgnoreCase);
            Assert.Null(Environment.GetEnvironmentVariable("ORT_DYLIB_PATH"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Environment.SetEnvironmentVariable("ORT_DYLIB_PATH", originalOrt);
        }
    }

    [Fact]
    public void PrepareForLoad_ScrubsChildEnvironmentDictionary()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var qnnDir = AppPaths.NpuDir;
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["PATH"] = qnnDir + ";C:\\Windows\\System32",
            ["ORT_DYLIB_PATH"] = Path.Combine(qnnDir, "onnxruntime.dll"),
            ["USERPROFILE"] = @"C:\Users\test",
        };

        SherpaNativeEnvironmentScrubber.PrepareForLoad(environment);

        Assert.DoesNotContain(qnnDir, environment["PATH"]!, StringComparison.OrdinalIgnoreCase);
        Assert.False(environment.ContainsKey("ORT_DYLIB_PATH"));
        Assert.Equal(@"C:\Users\test", environment["USERPROFILE"]);
    }
}
