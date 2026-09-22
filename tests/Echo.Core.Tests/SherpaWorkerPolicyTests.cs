namespace echo.Core.Tests;

public class SherpaWorkerPolicyTests
{
    [Fact]
    public void ShouldIsolate_IsFalseOnNonWindowsArm64Runtime()
    {
        Assert.False(echo.Platform.Windows.SherpaWorkerPolicy.ShouldIsolate());
    }

    [Theory]
    [InlineData("gigaam", true)]
    [InlineData("whisper", true)]
    [InlineData("omnilingual", true)]
    [InlineData("parakeet_npu", false)]
    public void IsSherpaEngineId_ClassifiesEngines(string engineId, bool expected)
    {
        Assert.Equal(expected, echo.Platform.Windows.SherpaWorkerPolicy.IsSherpaEngineId(engineId));
    }
}
