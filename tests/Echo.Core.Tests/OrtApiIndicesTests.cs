using echo.Engines.ParakeetNpu.Ort;

namespace echo.Core.Tests;

public class OrtApiIndicesTests
{
    [Fact]
    public void Indices_MatchOnnxRuntime_1_24_4_Header()
    {
        Assert.Equal(1, OrtApiIndices.GetErrorCode);
        Assert.Equal(2, OrtApiIndices.GetErrorMessage);
        Assert.Equal(3, OrtApiIndices.CreateEnv);
        Assert.Equal(301, OrtApiIndices.RegisterExecutionProviderLibrary);
        Assert.Equal(303, OrtApiIndices.GetEpDevices);
        Assert.Equal(304, OrtApiIndices.SessionOptionsAppendExecutionProviderV2);
        Assert.Equal(307, OrtApiIndices.HardwareDeviceType);
        Assert.Equal(312, OrtApiIndices.EpDeviceEpName);
        Assert.Equal(316, OrtApiIndices.EpDeviceDevice);
        Assert.Equal(93, OrtApiIndices.ReleaseStatus);
    }
}
