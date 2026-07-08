namespace echo.Abstractions.Platform;

public interface ITextInjector
{
    Task InjectAsync(string text, string method, CancellationToken cancellationToken = default);
}
