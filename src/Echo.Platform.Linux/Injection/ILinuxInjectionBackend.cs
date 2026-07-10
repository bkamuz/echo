using echo.Abstractions.Platform;

namespace echo.Platform.Linux.Injection;

internal interface ILinuxInjectionBackend
{
    string Name { get; }

    bool IsAvailable { get; }

    TextInjectionResult? TryInject(
        string text,
        string method,
        int typeDelayMs,
        CancellationToken cancellationToken);
}
