using echo.Abstractions.Platform;

namespace echo.Platform.Linux.Injection;

internal sealed class AtSpiInjectionBackend : ILinuxInjectionBackend
{
    public bool IsAvailable => LinuxAtSpiInserter.IsAvailable;

    public TextInjectionResult? TryInject(
        string text,
        string method,
        int typeDelayMs,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return null;
        }

        return LinuxAtSpiInserter.TryInsert(text, cancellationToken: cancellationToken)
            ? TextInjectionResult.AutoPasted
            : null;
    }
}
