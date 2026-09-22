namespace echo.Core.Tests;

public class SherpaWorkerPolicyTests
{
    [Fact]
    public void ShouldIsolate_IsFalseOnNonWindowsArm64Runtime()
    {
        Assert.False(echo.Platform.Windows.SherpaWorkerPolicy.ShouldIsolate());
    }
}
