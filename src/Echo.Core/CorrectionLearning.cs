using System.Globalization;
using System.Text.RegularExpressions;

namespace echo.Core;

public sealed record CorrectionCandidate(string From, string To);

/// <summary>
/// Word-level diff between what Echo inserted and what the user turned it into.
/// Only short replacements (1–3 words → 1–3 words) survive; case-only, punctuation-only,
/// pure insertions/deletions and large rewrites are dropped.
/// </summary>
public static class CorrectionExtractor
{
    public const int MaxPhraseWords = 3;
    private static readonly Regex WordRegex = new(@"[\p{L}\p{N}][\p{L}\p{N}_'’\-]*", RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Tokenize(string text) =>
        WordRegex.Matches(text).Select(m => m.Value.Trim('-', '\'', '’')).Where(w => w.Length > 0).ToList();

    private static string Norm(string w) => w.ToLower(CultureInfo.InvariantCulture).Replace('ё', 'е');

    public static IReadOnlyList<CorrectionCandidate> Extract(string injected, string edited)
    {
        var a = Tokenize(injected);
        var b = Tokenize(edited);
        if (a.Count == 0 || b.Count == 0)
        {
            return [];
        }

        // LCS alignment on normalized tokens.
        var n = a.Count;
        var m = b.Count;
        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                dp[i, j] = Norm(a[i]) == Norm(b[j])
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        var common = dp[0, 0];
        // Large rewrite: fewer than half of the inserted words kept.
        if (common * 2 < n)
        {
            return [];
        }

        var hunks = new List<(List<string> Del, List<string> Ins)>();
        var del = new List<string>();
        var ins = new List<string>();
        int x = 0, y = 0;
        void Flush()
        {
            if (del.Count > 0 || ins.Count > 0)
            {
                hunks.Add((del, ins));
                del = [];
                ins = [];
            }
        }

        while (x < n || y < m)
        {
            if (x < n && y < m && Norm(a[x]) == Norm(b[y]))
            {
                Flush();
                x++;
                y++;
            }
            else if (y < m && (x >= n || dp[x, y + 1] >= dp[x + 1, y]))
            {
                ins.Add(b[y++]);
            }
            else
            {
                del.Add(a[x++]);
            }
        }

        Flush();

        var result = new List<CorrectionCandidate>();
        foreach (var (d, i) in hunks)
        {
            if (d.Count is 0 or > MaxPhraseWords || i.Count is 0 or > MaxPhraseWords)
            {
                continue; // pure insertion/deletion or too long
            }

            var from = string.Join(' ', d);
            var to = string.Join(' ', i);
            if (Norm(from).Replace(" ", "") == Norm(to).Replace(" ", ""))
            {
                continue; // case / spacing only
            }

            if (result.All(r => r.From != from || r.To != to))
            {
                result.Add(new CorrectionCandidate(from, to));
            }
        }

        // Too many separate edits in one span looks like a rewrite, not corrections.
        return result.Count > 3 ? [] : result;
    }
}

/// <summary>
/// Tracks the inserted span inside a control's text using the text around it captured right after
/// paste. Only the span between that prefix and suffix is ever handed to the extractor.
/// </summary>
public sealed class InjectedSpanTracker
{
    private readonly string _prefix;
    private readonly string _suffix;

    private InjectedSpanTracker(string prefix, string suffix)
    {
        _prefix = prefix;
        _suffix = suffix;
    }

    public static InjectedSpanTracker? Create(string baseline, string injected)
    {
        var needle = injected.Trim();
        if (needle.Length == 0)
        {
            return null;
        }

        var idx = baseline.LastIndexOf(needle, StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        return new InjectedSpanTracker(baseline[..idx], baseline[(idx + needle.Length)..]);
    }

    /// <summary>Returns the current text of the injected span, or null when the surroundings changed.</summary>
    public string? CurrentSpan(string current)
    {
        if (current.Length < _prefix.Length + _suffix.Length
            || !current.StartsWith(_prefix, StringComparison.Ordinal)
            || !current.EndsWith(_suffix, StringComparison.Ordinal))
        {
            return null;
        }

        return current.Substring(_prefix.Length, current.Length - _prefix.Length - _suffix.Length);
    }
}
