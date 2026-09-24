using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace echo.Engines.ParakeetNpu.Ort;

/// <summary>
/// Ensures COM is initialized on the calling thread before QNN/ORT native calls.
/// </summary>
internal static class ComApartmentHelper
{
    internal const uint CoinitMultithreaded = 0x0;
    internal const uint CoinitApartmentthreaded = 0x2;
    internal const uint CoinitDisableOle1Dde = 0x4;
    internal const uint CoinitSpeedOverMemory = 0x8;

    internal const int HResultOk = 0;
    internal const int HResultFalse = 1;
    internal const int RpcEChangedMode = unchecked((int)0x80010106);
    internal const int CoENotInitialized = unchecked((int)0x800401F0);

    internal static uint DefaultCoInitFlags => CoinitMultithreaded | CoinitDisableOle1Dde;

    public static ComApartmentLease EnsureInitialized(ILogger? logger = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("COM apartment initialization is Windows-only.");
        }

        var before = TryDescribeApartment();
        logger?.LogDebug(
            "COM apartment before CoInitializeEx: {ApartmentDescription}",
            before);

        var hr = CoInitializeEx(IntPtr.Zero, DefaultCoInitFlags);
        var ownsInitialization = hr switch
        {
            HResultOk => true,
            HResultFalse => false,
            RpcEChangedMode => false,
            _ => throw new InvalidOperationException(
                $"CoInitializeEx failed: 0x{hr:X8} ({DescribeHresult(hr)}). "
                + $"Thread apartment before init: {before}."),
        };

        if (hr == RpcEChangedMode)
        {
            logger?.LogInformation(
                "CoInitializeEx returned RPC_E_CHANGED_MODE (0x80010106); "
                + "thread already initialized COM with a different apartment model.");
        }
        else if (hr == HResultFalse)
        {
            logger?.LogDebug(
                "CoInitializeEx returned S_FALSE; COM was already initialized on this thread.");
        }

        var after = TryDescribeApartment();
        logger?.LogInformation(
            "COM apartment ensured for QNN: initHr=0x{InitHr:X8} ({InitHrName}), ownsInit={OwnsInit}, after={AfterApartment}",
            hr,
            DescribeHresult(hr),
            ownsInitialization,
            after);

        return new ComApartmentLease(ownsInitialization);
    }

    internal static string DescribeHresult(int hr) => hr switch
    {
        HResultOk => "S_OK",
        HResultFalse => "S_FALSE",
        RpcEChangedMode => "RPC_E_CHANGED_MODE",
        CoENotInitialized => "CO_E_NOTINITIALIZED",
        unchecked((int)0x80070057) => "E_INVALIDARG",
        _ => OperatingSystem.IsWindows()
            ? Marshal.GetPInvokeErrorMessage(hr)
            : $"HRESULT 0x{hr:X8}",
    };

    internal static string TryDescribeApartment()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "unavailable (not Windows)";
        }

        var hr = CoGetApartmentType(out var type, out var qualifier);
        if (hr == CoENotInitialized)
        {
            return "not initialized";
        }

        if (hr != HResultOk)
        {
            return $"CoGetApartmentType failed: 0x{hr:X8} ({DescribeHresult(hr)})";
        }

        return $"{DescribeApartmentType(type)} (qualifier={DescribeApartmentQualifier(qualifier)})";
    }

    private static string DescribeApartmentType(ApartmentType type) => type switch
    {
        ApartmentType.Sta => "STA",
        ApartmentType.Mta => "MTA",
        ApartmentType.Na => "NA",
        ApartmentType.MainSta => "MainSTA",
        _ => type.ToString(),
    };

    private static string DescribeApartmentQualifier(ApartmentTypeQualifier qualifier) => qualifier switch
    {
        ApartmentTypeQualifier.None => "none",
        ApartmentTypeQualifier.ImplicitMta => "implicit MTA",
        ApartmentTypeQualifier.ApartmentsInSameThread => "apartments in same thread",
        _ => qualifier.ToString(),
    };

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    private static extern int CoGetApartmentType(
        out ApartmentType apartmentType,
        out ApartmentTypeQualifier apartmentQualifier);

    private enum ApartmentType
    {
        Sta = 0,
        Mta = 1,
        Na = 2,
        MainSta = 3,
    }

    private enum ApartmentTypeQualifier
    {
        None = 0,
        ImplicitMta = 1,
        ApartmentsInSameThread = 2,
    }

    internal sealed class ComApartmentLease : IDisposable
    {
        private bool _ownsInitialization;
        private bool _disposed;

        internal ComApartmentLease(bool ownsInitialization)
        {
            _ownsInitialization = ownsInitialization;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_ownsInitialization)
            {
                CoUninitialize();
            }

            _disposed = true;
            _ownsInitialization = false;
        }
    }
}
