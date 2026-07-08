using echo.Abstractions.Engines;

namespace echo.Engines;

internal static class SherpaProviderHelper
{
    public static string ResolveSherpaProvider(string configDevice) =>
        ExecutionProviderResolver.ToSherpaProvider(ExecutionProviderResolver.FromConfigDevice(configDevice));
}
