namespace echo.Abstractions.Core;

/// <summary>
/// Invariant progress protocol for IProgress&lt;string&gt;.
/// UI localizes these tokens; never match on translated display text.
/// </summary>
public static class ProgressMessages
{
    public const string DonePrefix = "DONE:";
    public const string WorkingPrefix = "WORKING:";

    public static string Done(string? detail = null) =>
        string.IsNullOrEmpty(detail) ? DonePrefix : DonePrefix + detail;

    public static bool IsDone(string? status) =>
        !string.IsNullOrWhiteSpace(status)
        && status.TrimStart().StartsWith(DonePrefix, StringComparison.Ordinal);

    public static string? GetDoneDetail(string? status)
    {
        if (!IsDone(status))
        {
            return null;
        }

        var trimmed = status!.TrimStart();
        return trimmed.Length <= DonePrefix.Length
            ? string.Empty
            : trimmed[DonePrefix.Length..];
    }

    public static string Downloading(string name) => $"{WorkingPrefix}Downloading:{name}";

    public static string Saving() => $"{WorkingPrefix}Saving";

    public static string LoadingModel() => $"{WorkingPrefix}LoadingModel";

    public static string PreparingDirectMl() => $"{WorkingPrefix}PreparingDirectMl";

    public static string DownloadingUpdate() => $"{WorkingPrefix}DownloadingUpdate";

    public static string PreparingUpdate() => $"{WorkingPrefix}PreparingUpdate";

    public static string InstallingUpdate() => $"{WorkingPrefix}InstallingUpdate";

    public static bool TryParseWorking(string? status, out string kind, out string? arg)
    {
        kind = string.Empty;
        arg = null;
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        var trimmed = status.TrimStart();
        if (!trimmed.StartsWith(WorkingPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = trimmed[WorkingPrefix.Length..];
        var colon = rest.IndexOf(':');
        if (colon < 0)
        {
            kind = rest;
            return kind.Length > 0;
        }

        kind = rest[..colon];
        arg = rest[(colon + 1)..];
        return kind.Length > 0;
    }
}
