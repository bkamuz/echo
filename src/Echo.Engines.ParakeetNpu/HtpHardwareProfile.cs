using echo.Abstractions.Platform;

namespace echo.Engines.ParakeetNpu;

/// <summary>
/// Resolves Hexagon HTP generation, QNN runtime file set, and session EP options for the local SoC.
/// </summary>
public enum HexagonHtpGeneration
{
    V73,
    V81,
}

public sealed class HtpHardwareProfile
{
    public static readonly HtpHardwareProfile V73XElite = new(
        HexagonHtpGeneration.V73,
        htpArch: "73",
        socModel: "60",
        stubDll: "QnnHtpV73Stub.dll",
        skelSo: "libQnnHtpV73Skel.so",
        catalogCat: "libqnnhtpv73.cat",
        contextTarget: "Snapdragon X Elite (Hexagon V73)");

    public static readonly HtpHardwareProfile V81X2Elite = new(
        HexagonHtpGeneration.V81,
        htpArch: "81",
        socModel: "88",
        stubDll: "QnnHtpV81Stub.dll",
        skelSo: "libQnnHtpV81Skel.so",
        catalogCat: "libqnnhtpv81.cat",
        contextTarget: "Snapdragon X2 Elite (Hexagon V81)");

    private HtpHardwareProfile(
        HexagonHtpGeneration generation,
        string htpArch,
        string socModel,
        string stubDll,
        string skelSo,
        string catalogCat,
        string contextTarget)
    {
        Generation = generation;
        HtpArch = htpArch;
        SocModel = socModel;
        StubDll = stubDll;
        SkelSo = skelSo;
        CatalogCat = catalogCat;
        ContextTarget = contextTarget;
    }

    public HexagonHtpGeneration Generation { get; }
    public string HtpArch { get; }
    public string SocModel { get; }
    public string StubDll { get; }
    public string SkelSo { get; }
    public string CatalogCat { get; }
    public string ContextTarget { get; }

    public IReadOnlyList<string> RequiredHtpFiles => [StubDll, SkelSo, CatalogCat];

    public static HtpHardwareProfile Resolve(string? processorName = null)
    {
        if (processorName is null)
        {
            // TryReadProcessorName is Windows-only (registry); default V73 off-Windows.
            processorName = OperatingSystem.IsWindows()
                ? SnapdragonHardware.TryReadProcessorName()
                : null;
        }

        return SnapdragonProcessorMatcher.MayNeedAlternateHtpContext(processorName)
            ? V81X2Elite
            : V73XElite;
    }

    public static IReadOnlyList<string> SharedRequiredRuntimeFiles { get; } =
    [
        "onnxruntime.dll",
        "onnxruntime_providers_qnn.dll",
        "QnnHtp.dll",
        "QnnHtpPrepare.dll",
        "QnnHtpNetRunExtensions.dll",
        "QnnSystem.dll",
    ];

    public static IReadOnlyList<string> AllKnownHtpFiles { get; } =
    [
        "QnnHtpV73Stub.dll",
        "libQnnHtpV73Skel.so",
        "libqnnhtpv73.cat",
        "QnnHtpV81Stub.dll",
        "libQnnHtpV81Skel.so",
        "libqnnhtpv81.cat",
    ];

    public IReadOnlyList<string> RequiredRuntimeFiles()
    {
        var files = new List<string>(SharedRequiredRuntimeFiles);
        files.AddRange(RequiredHtpFiles);
        return files;
    }

    public static IReadOnlyList<string> FullRuntimeFileSet()
    {
        var files = new List<string>(SharedRequiredRuntimeFiles);
        files.AddRange(AllKnownHtpFiles);
        return files;
    }
}
