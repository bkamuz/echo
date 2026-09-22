using echo.Core;

namespace echo.Core.Tests;

public class StartupWarmupPolicyTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShouldDeferWarmup_DeferOnWindowsOnly(bool isWindows, bool expected)
    {
        Assert.Equal(expected, StartupWarmupPolicy.ShouldDeferWarmup(isWindows));
    }
}
