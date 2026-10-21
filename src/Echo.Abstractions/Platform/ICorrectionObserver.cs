namespace echo.Abstractions.Platform;

/// <summary>
/// Watches the focused control after a successful paste and reports user edits of the
/// inserted span. Implemented on Windows via UI Automation; absent elsewhere.
/// </summary>
public interface ICorrectionObserver
{
    void Observe(string injectedText);
}
