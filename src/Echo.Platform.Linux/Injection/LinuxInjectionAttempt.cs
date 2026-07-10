using echo.Abstractions.Platform;

namespace echo.Platform.Linux.Injection;

public sealed record LinuxInjectionAttempt(TextInjectionResult Result, string BackendName);
