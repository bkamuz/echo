using echo.Platform.Windows;

namespace echo.Core.Tests;

public class OrtQnnExportTests
{
    [Fact]
    public void IsPresent_RequiresOrtAndQnnProviderFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "echo-qnn-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            Assert.False(OrtQnnExport.IsPresent(tempDir));

            File.WriteAllText(Path.Combine(tempDir, "onnxruntime.dll"), string.Empty);
            Assert.False(OrtQnnExport.IsPresent(tempDir));

            File.WriteAllText(Path.Combine(tempDir, "onnxruntime_providers_qnn.dll"), string.Empty);
            Assert.True(OrtQnnExport.IsPresent(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
