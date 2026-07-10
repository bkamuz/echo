using echo.Abstractions.Platform;

namespace echo.Platform.MacOS;

public sealed class MacOsTextInjector : ITextInjector
{
    public Task<TextInjectionResult> InjectAsync(
        string text,
        string method,
        int typeDelayMs = 0,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(TextInjectionResult.Failed(
            "Text injection on macOS requires Accessibility API implementation."));
}
