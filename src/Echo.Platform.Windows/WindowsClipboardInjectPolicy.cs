using echo.Abstractions.Platform;

namespace echo.Platform.Windows;

public enum ClipboardPasteOutcome
{
    FailedBeforePaste,
    Pasted,
}

/// <summary>
/// Pure policy for Windows clipboard inject vs type fallback.
/// After Ctrl+V, paste already happened — restore/clear must not trigger typing.
/// </summary>
public static class WindowsClipboardInjectPolicy
{
    /// <summary>
    /// Returns a final result when clipboard path is done, or null when the caller should type.
    /// </summary>
    public static TextInjectionResult? Resolve(string method, ClipboardPasteOutcome outcome)
    {
        if (outcome == ClipboardPasteOutcome.Pasted)
        {
            return TextInjectionResult.AutoPasted;
        }

        if (method is "clipboard")
        {
            return TextInjectionResult.Failed("Loc.Inject.Failed");
        }

        // "auto" (and any other non-clipboard method that attempted clipboard): type fallback.
        return null;
    }
}
