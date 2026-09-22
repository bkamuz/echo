namespace echo.Abstractions.Engines;

public enum ExecutionProvider
{
    Cpu,
    DirectMl,
    Npu,
}

public static class ExecutionProviderResolver
{
    public const string CpuDevice = "cpu";
    public const string DirectMlDevice = "directml";
    public const string NpuDevice = "npu";

    /// <summary>Sherpa-ONNX provider string for Qualcomm QNN / HTP (when built with QNN support).</summary>
    public const string QnnSherpaProvider = "qnn";

    public static IReadOnlyList<string> AllDeviceIds { get; } = [CpuDevice, DirectMlDevice, NpuDevice];

    public static string ToSherpaProvider(ExecutionProvider provider) => provider switch
    {
        ExecutionProvider.DirectMl => "directml",
        ExecutionProvider.Npu => QnnSherpaProvider,
        ExecutionProvider.Cpu => "cpu",
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };

    public static ExecutionProvider FromConfigDevice(string? device) => device switch
    {
        DirectMlDevice => ExecutionProvider.DirectMl,
        NpuDevice or QnnSherpaProvider => ExecutionProvider.Npu,
        CpuDevice => ExecutionProvider.Cpu,
        "cuda" => ExecutionProvider.Cpu,
        _ => ExecutionProvider.Cpu,
    };

    public static string ToConfigDevice(ExecutionProvider provider) => provider switch
    {
        ExecutionProvider.DirectMl => DirectMlDevice,
        ExecutionProvider.Npu => NpuDevice,
        ExecutionProvider.Cpu => CpuDevice,
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };
}
