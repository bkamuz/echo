namespace echo.Abstractions.Platform;

/// <summary>
/// Matches Snapdragon Copilot+ processor marketing names and part numbers for HTP eligibility heuristics.
/// </summary>
public static class SnapdragonProcessorMatcher
{
    /// <summary>
    /// True when the processor string looks like Snapdragon X/X2 Elite family (HTP-capable Copilot+ PCs).
    /// </summary>
    public static bool IsSupportedHtpProcessor(string? processorName)
    {
        if (string.IsNullOrWhiteSpace(processorName))
        {
            return false;
        }

        var normalized = processorName.ToLowerInvariant();
        if (!normalized.Contains("snapdragon"))
        {
            return false;
        }

        return normalized.Contains("x elite")
            || normalized.Contains("x2 elite")
            || normalized.Contains("x1e")
            || normalized.Contains("x2e");
    }

    /// <summary>
    /// True when the HTP context binary may target a newer Hexagon generation than V73 (e.g. X2 Elite).
    /// </summary>
    public static bool MayNeedAlternateHtpContext(string? processorName)
    {
        if (string.IsNullOrWhiteSpace(processorName))
        {
            return false;
        }

        var normalized = processorName.ToLowerInvariant();
        return normalized.Contains("x2 elite") || normalized.Contains("x2e");
    }
}
