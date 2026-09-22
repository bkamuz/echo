using echo.Abstractions.Platform;

namespace echo.Core.Tests;

public class SnapdragonProcessorMatcherTests
{
    [Theory]
    [InlineData("Snapdragon(R) X Elite X1E78100")]
    [InlineData("Snapdragon X Elite")]
    [InlineData("Qualcomm Snapdragon X1E80100")]
    [InlineData("Snapdragon(R) X2 Elite Extreme - X2E94100 - Qualcomm Oryon(TM) CPU")]
    [InlineData("Snapdragon X2 Elite")]
    [InlineData("Snapdragon X2E94100")]
    public void IsSupportedHtpProcessor_AcceptsEliteFamilyNames(string processor)
    {
        Assert.True(SnapdragonProcessorMatcher.IsSupportedHtpProcessor(processor));
    }

    [Theory]
    [InlineData("Snapdragon X Plus X1P42100")]
    [InlineData("Intel Core Ultra 7 165U")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsSupportedHtpProcessor_RejectsNonEliteProcessors(string processor)
    {
        Assert.False(SnapdragonProcessorMatcher.IsSupportedHtpProcessor(processor));
    }

    [Theory]
    [InlineData("Snapdragon(R) X2 Elite Extreme - X2E94100", true)]
    [InlineData("Snapdragon(R) X Elite X1E78100", false)]
    [InlineData("Snapdragon X Plus X1P42100", false)]
    public void MayNeedAlternateHtpContext_DetectsX2Generation(string processor, bool expected)
    {
        Assert.Equal(expected, SnapdragonProcessorMatcher.MayNeedAlternateHtpContext(processor));
    }

    [Fact]
    public void X2EliteExtreme_DoesNotMatchLegacyXEliteSubstringOnly()
    {
        var processor = "Snapdragon(R) X2 Elite Extreme - X2E94100";
        var normalized = processor.ToLowerInvariant();

        Assert.DoesNotContain("x elite", normalized.Replace("x2 elite", string.Empty, StringComparison.Ordinal));
        Assert.Contains("x2 elite", normalized, StringComparison.Ordinal);
        Assert.True(SnapdragonProcessorMatcher.IsSupportedHtpProcessor(processor));
    }
}
