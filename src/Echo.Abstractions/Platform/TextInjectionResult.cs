namespace echo.Abstractions.Platform;

public enum TextInjectionOutcome
{
    AutoPasted,
    ClipboardOnly,
    Failed,
}

public sealed record TextInjectionResult(TextInjectionOutcome Outcome, string? Message = null)
{
    public static TextInjectionResult AutoPasted { get; } = new(TextInjectionOutcome.AutoPasted);

    public static TextInjectionResult ClipboardOnly(string message) =>
        new(TextInjectionOutcome.ClipboardOnly, message);

    public static TextInjectionResult Failed(string message) =>
        new(TextInjectionOutcome.Failed, message);
}
