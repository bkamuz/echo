using echo.Engines.ParakeetNpu.Ort;

namespace echo.Core.Tests;

public class ComApartmentHelperTests
{
    [Fact]
    public void CoinitDisableOle1Dde_UsesWin32ValueNotLegacyTypo()
    {
        // COINIT_DISABLE_OLE1DDE is 0x4 in objbase.h. 0x100 was passed historically and
        // caused CoInitializeEx to return E_INVALIDARG (0x80070057) on Windows arm64.
        Assert.Equal(0x4u, ComApartmentHelper.CoinitDisableOle1Dde);
        Assert.Equal(0x4u, ComApartmentHelper.DefaultCoInitFlags);
    }

    [Fact]
    public void DescribeHresult_MapsKnownComResults()
    {
        Assert.Equal("S_OK", ComApartmentHelper.DescribeHresult(ComApartmentHelper.HResultOk));
        Assert.Equal("S_FALSE", ComApartmentHelper.DescribeHresult(ComApartmentHelper.HResultFalse));
        Assert.Equal("RPC_E_CHANGED_MODE", ComApartmentHelper.DescribeHresult(ComApartmentHelper.RpcEChangedMode));
        Assert.Equal("CO_E_NOT_INITIALIZED", ComApartmentHelper.DescribeHresult(ComApartmentHelper.CoENotInitialized));
        Assert.Equal("E_INVALIDARG", ComApartmentHelper.DescribeHresult(unchecked((int)0x80070057)));
    }

    [Fact]
    public void TryDescribeApartment_ReturnsReadableStateOnAllPlatforms()
    {
        var description = ComApartmentHelper.TryDescribeApartment();
        Assert.False(string.IsNullOrWhiteSpace(description));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Contains("not Windows", description, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void EnsureInitialized_DoesNotThrowOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var lease = ComApartmentHelper.EnsureInitialized();
        using var secondLease = ComApartmentHelper.EnsureInitialized();
    }
}
