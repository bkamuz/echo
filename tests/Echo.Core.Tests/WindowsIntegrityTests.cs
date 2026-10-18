using echo.Platform.Windows;

namespace echo.Core.Tests;

public sealed class WindowsIntegrityTests
{
    [Theory]
    [InlineData(WindowsIntegrity.Medium, WindowsIntegrity.High, true)]
    [InlineData(WindowsIntegrity.Medium, WindowsIntegrity.AccessDenied, true)]
    [InlineData(WindowsIntegrity.Medium, WindowsIntegrity.Medium, false)]
    [InlineData(WindowsIntegrity.High, WindowsIntegrity.High, false)]
    [InlineData(WindowsIntegrity.High, WindowsIntegrity.AccessDenied, false)]
    [InlineData(WindowsIntegrity.High, WindowsIntegrity.Medium, false)]
    [InlineData(WindowsIntegrity.Unknown, WindowsIntegrity.High, false)]
    [InlineData(WindowsIntegrity.Medium, WindowsIntegrity.Unknown, false)]
    public void IsBlockedByUipi_MatchesIntegrityOrdering(int own, int target, bool expected) =>
        Assert.Equal(expected, WindowsIntegrity.IsBlockedByUipi(own, target));

    [Theory]
    [InlineData(0x2000, "Medium(0x2000)")]
    [InlineData(0x3000, "High(0x3000)")]
    [InlineData(WindowsIntegrity.AccessDenied, "denied(likely High)")]
    public void Describe_IsReadable(int level, string expected) =>
        Assert.Equal(expected, WindowsIntegrity.Describe(level));

    [Fact]
    public void NonWindows_ReturnsUnknown()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(WindowsIntegrity.Unknown, WindowsIntegrity.GetProcessIntegrity(1));
    }
}
