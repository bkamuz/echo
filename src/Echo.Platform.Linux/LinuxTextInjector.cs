using echo.Abstractions.Platform;

namespace echo.Platform.Linux;

public sealed class LinuxTextInjector : ITextInjector
{
    public Task InjectAsync(string text, string method, int typeDelayMs = 0, CancellationToken cancellationToken = default) =>
        throw new PlatformNotSupportedException("Text injection on Linux requires X11/Wayland implementation.");
}
