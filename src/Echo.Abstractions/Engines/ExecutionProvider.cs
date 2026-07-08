namespace echo.Abstractions.Engines;

public enum ExecutionProvider
{
    Cpu,
    DirectMl,
}

public static class ExecutionProviderResolver
{
    public const string CpuDevice = "cpu";
    public const string DirectMlDevice = "directml";

    public static IReadOnlyList<string> AllDeviceIds { get; } = [CpuDevice, DirectMlDevice];

    public static string ToSherpaProvider(ExecutionProvider provider) => provider switch
    {
        ExecutionProvider.DirectMl => "directml",
        ExecutionProvider.Cpu => "cpu",
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };

    public static ExecutionProvider FromConfigDevice(string? device) => device switch
    {
        DirectMlDevice => ExecutionProvider.DirectMl,
        CpuDevice => ExecutionProvider.Cpu,
        "cuda" => ExecutionProvider.Cpu,
        _ => ExecutionProvider.Cpu,
    };

    public static string ToConfigDevice(ExecutionProvider provider) => provider switch
    {
        ExecutionProvider.DirectMl => DirectMlDevice,
        ExecutionProvider.Cpu => CpuDevice,
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };
}
