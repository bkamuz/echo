namespace echo.Abstractions.Platform;

/// <summary>
/// Watches the focused control after a successful paste and reports user edits of the
/// inserted span. Windows: UI Automation; Linux: AT-SPI2; macOS: AXUIElement.
/// </summary>
public interface ICorrectionObserver
{
    void Observe(string injectedText);
}
