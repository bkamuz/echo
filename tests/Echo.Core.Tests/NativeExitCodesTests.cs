using echo.Abstractions.Core;

namespace echo.Core.Tests;

public class NativeExitCodesTests
{
    [Fact]
    public void Describe_AccessViolation_IncludesStatusName()
    {
        const int accessViolation = -1073741819;

        var description = NativeExitCodes.Describe(accessViolation);

        Assert.Contains("STATUS_ACCESS_VIOLATION", description);
        Assert.Contains("C0000005", description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0, "success")]
    [InlineData(-1073741515, "STATUS_DLL_NOT_FOUND")]
    public void Describe_KnownCodes(int exitCode, string expectedFragment)
    {
        var description = NativeExitCodes.Describe(exitCode);

        Assert.Contains(expectedFragment, description);
    }
}
