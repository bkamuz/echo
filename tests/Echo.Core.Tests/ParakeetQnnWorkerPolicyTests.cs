namespace echo.Core.Tests;

public class ParakeetQnnWorkerPolicyTests
{
    [Theory]
    [InlineData("npu", true)]
    [InlineData("cpu", false)]
    [InlineData("NPU", true)]
    public void IsNpuDevice_ClassifiesConfigDevice(string device, bool expected)
    {
        Assert.Equal(expected, echo.Platform.Windows.ParakeetQnnWorkerPolicy.IsNpuDevice(device));
    }
}
