using echo.Abstractions.Core;
using echo.Platform.Windows;

namespace echo.Core.Tests;

public class ParakeetRuntimeProbeTests
{
    [Fact]
    public void IsRuntimeInstalled_ReturnsFalse_WhenDirectoryMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "echo-parakeet-probe-" + Guid.NewGuid().ToString("N"));
        var previousAppData = Environment.GetEnvironmentVariable("APPDATA");
        var previousXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Environment.SetEnvironmentVariable("APPDATA", tempRoot);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", tempRoot);

        try
        {
            Assert.False(ParakeetRuntimeProbe.IsRuntimeInstalled);
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

    [Fact]
    public void IsRuntimeInstalled_ReturnsFalse_WhenRequiredFileIsEmpty()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "echo-parakeet-probe-" + Guid.NewGuid().ToString("N"));
        var qnnDir = Path.Combine(tempRoot, "echo", "qnn");
        var previousAppData = Environment.GetEnvironmentVariable("APPDATA");
        var previousXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Environment.SetEnvironmentVariable("APPDATA", tempRoot);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", tempRoot);

        try
        {
            Directory.CreateDirectory(qnnDir);
            File.WriteAllBytes(Path.Combine(qnnDir, "onnxruntime.dll"), []);
            File.WriteAllText(Path.Combine(qnnDir, "onnxruntime_providers_qnn.dll"), "stub");

            Assert.False(ParakeetRuntimeProbe.IsRuntimeInstalled);
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

    [Fact]
    public void IsRuntimeInstalled_ReturnsTrue_WhenRequiredFilesPresent()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "echo-parakeet-probe-" + Guid.NewGuid().ToString("N"));
        var qnnDir = Path.Combine(tempRoot, "echo", "qnn");
        var previousAppData = Environment.GetEnvironmentVariable("APPDATA");
        var previousXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Environment.SetEnvironmentVariable("APPDATA", tempRoot);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", tempRoot);

        try
        {
            Directory.CreateDirectory(qnnDir);
            foreach (var file in new[]
                     {
                         "onnxruntime.dll",
                         "onnxruntime_providers_qnn.dll",
                         "QnnHtp.dll",
                         "QnnHtpPrepare.dll",
                         "QnnHtpNetRunExtensions.dll",
                         "QnnHtpV73Stub.dll",
                         "libQnnHtpV73Skel.so",
                         "libqnnhtpv73.cat",
                         "QnnHtpV81Stub.dll",
                         "libQnnHtpV81Skel.so",
                         "libqnnhtpv81.cat",
                         "QnnSystem.dll",
                     })
            {
                File.WriteAllText(Path.Combine(qnnDir, file), "stub");
            }

            Assert.True(ParakeetRuntimeProbe.IsRuntimeInstalled);
            Assert.Equal(qnnDir, AppPaths.NpuDir);
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
