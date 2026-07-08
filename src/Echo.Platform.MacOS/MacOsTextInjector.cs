using echo.Abstractions.Platform;

namespace echo.Platform.MacOS;

public sealed class MacOsTextInjector : ITextInjector
{
    public Task InjectAsync(string text, string method, int typeDelayMs = 0, CancellationToken cancellationToken = default) =>
        throw new PlatformNotSupportedException("Text injection on macOS requires Accessibility API implementation.");
}
