namespace echo.Abstractions.Platform;

public interface IApplicationClipboard
{
    bool IsAvailable { get; }

    ValueTask SetTextAsync(string text, CancellationToken cancellationToken = default);
}
