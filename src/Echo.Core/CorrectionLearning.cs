using System.Globalization;
using System.Text.RegularExpressions;

namespace echo.Core;

public sealed record CorrectionCandidate(string From, string To);

/// <summary>
/// Word-level diff between what Echo inserted and what the user turned it into.
/// Every changed span becomes its own candidate; only short replacements (1–3 words → 1–3 words)
/// survive. Case-only, punctuation-only, pure insertions/deletions and full rewrites are dropped.
/// </summary>
public static class CorrectionExtractor
{
    public const int MaxPhraseWords = 3;
    private static readonly Regex WordRegex = new(@"[\p{L}\p{N}][\p{L}\p{N}_'’\-]*", RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Tokenize(string text) =>
        WordRegex.Matches(text).Select(m => m.Value.Trim('-', '\'', '’')).Where(w => w.Length > 0).ToList();

    private static bool IsTypoFix(string from, string to)
    {
        var a = Norm(from);
        var b = Norm(to);
        if (a == b)
        {
            return true;
        }

        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                cur[j] = Math.Min(Math.Min(cur[j - 1], prev[j]) + 1, prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            (prev, cur) = (cur, prev);
        }

        return prev[b.Length] * 2 <= Math.Max(a.Length, b.Length);
    }

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
        // Complete rewrite of a longer text: nothing kept, so no word-level fixes to learn from.
        if (common == 0 && n > MaxPhraseWords)
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

        // Adjacent word-by-word typo fixes ("qick brwn" → "quick brown") are separate corrections;
        // anything else (merges, splits, transliterations like "вижу ал студио" → "Visual Studio") stays a phrase.
        var split = new List<(List<string> Del, List<string> Ins)>();
        foreach (var (d, i) in hunks)
        {
            if (d.Count > 1 && d.Count == i.Count && d.Zip(i).All(p => IsTypoFix(p.First, p.Second)))
            {
                split.AddRange(d.Zip(i).Select(p => (new List<string> { p.First }, new List<string> { p.Second })));
            }
            else
            {
                split.Add((d, i));
            }
        }

        var result = new List<CorrectionCandidate>();
        foreach (var (d, i) in split)
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

            if (result.All(r => Norm(r.From) != Norm(from) || Norm(r.To) != Norm(to)))
            {
                result.Add(new CorrectionCandidate(from, to));
            }
        }

        // Every separate replacement is its own suggestion (several fixes in one dictation are all kept).
        return result;
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

/// <summary>
/// One edit session after a paste: remembers the latest readable state of the inserted span and ends
/// after <c>idle</c> without changes (the timer restarts on every edit), so several fixes are all seen.
/// </summary>
public sealed class CorrectionSession
{
    public static readonly TimeSpan HardCap = TimeSpan.FromMinutes(10);
    private readonly TimeSpan _idle;
    private DateTime _lastChange = DateTime.UtcNow;

    public CorrectionSession(TimeSpan idle) => _idle = idle;

    public string? LastSpan { get; private set; }

    public bool IsIdle => DateTime.UtcNow - _lastChange >= _idle;

    public void Observe(string? span)
    {
        if (span is null || span == LastSpan)
        {
            return;
        }

        LastSpan = span;
        _lastChange = DateTime.UtcNow;
    }
}
