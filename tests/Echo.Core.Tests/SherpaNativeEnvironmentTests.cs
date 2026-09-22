using echo.Abstractions.Core;
using echo.Engines;

namespace echo.Core.Tests;

public class SherpaNativeEnvironmentTests
{
    [Fact]
    public void ResolveNumThreads_IsAtLeastOne()
    {
        Assert.True(SherpaNativeEnvironment.ResolveNumThreads() >= 1);
    }

    [Fact]
    public void PrepareForLoad_RemovesEchoQnnDirFromPath_OnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var originalPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var qnnDir = AppPaths.NpuDir;
        Environment.SetEnvironmentVariable("PATH", qnnDir + Path.PathSeparator + originalPath);
        Environment.SetEnvironmentVariable("ORT_DYLIB_PATH", Path.Combine(qnnDir, "onnxruntime.dll"));

        try
        {
            SherpaNativeEnvironment.PrepareForLoad();

            var updatedPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            Assert.DoesNotContain(qnnDir, updatedPath, StringComparison.OrdinalIgnoreCase);
            Assert.Null(Environment.GetEnvironmentVariable("ORT_DYLIB_PATH"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Environment.SetEnvironmentVariable("ORT_DYLIB_PATH", null);
        }
    }
}
