namespace echo.Abstractions.Platform;

public interface ITextInjector
{
    Task InjectAsync(string text, string method, int typeDelayMs = 0, CancellationToken cancellationToken = default);
}
