using echo.Engines.ParakeetNpu;

namespace echo.Core.Tests;

public class HtpHardwareProfileTests
{
    [Theory]
    [InlineData("Snapdragon(R) X Elite X1E78100", "73", "60", "QnnHtpV73Stub.dll")]
    [InlineData("Snapdragon(R) X2 Elite Extreme - X2E94100 - Qualcomm Oryon(TM) CPU", "81", "88", "QnnHtpV81Stub.dll")]
    public void Resolve_SelectsGenerationFromProcessor(
        string processor,
        string expectedArch,
        string expectedSoc,
        string expectedStub)
    {
        var profile = HtpHardwareProfile.Resolve(processor);

        Assert.Equal(expectedArch, profile.HtpArch);
        Assert.Equal(expectedSoc, profile.SocModel);
        Assert.Equal(expectedStub, profile.StubDll);
        Assert.Contains(expectedStub, profile.RequiredRuntimeFiles());
    }

    [Fact]
    public void FullRuntimeFileSet_IncludesBothV73AndV81HtpPairs()
    {
        var files = HtpHardwareProfile.FullRuntimeFileSet();

        Assert.Contains("QnnHtpV73Stub.dll", files);
        Assert.Contains("QnnHtpV81Stub.dll", files);
        Assert.Contains("libQnnHtpV81Skel.so", files);
        Assert.Contains("libqnnhtpv81.cat", files);
    }
}
