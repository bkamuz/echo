using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using echo.Abstractions.Core;

namespace echo.Core;

/// <summary>One "as recognized" → "as it should be" pair.</summary>
public sealed class DictionaryEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("from")] public string From { get; set; } = string.Empty;
    [JsonPropertyName("to")] public string To { get; set; } = string.Empty;
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("uses")] public int Uses { get; set; }
    [JsonPropertyName("last_used")] public DateTimeOffset? LastUsed { get; set; }
    [JsonPropertyName("created")] public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Where the pair came from: "manual", "history", "import". Stage 2 (auto-learned corrections)
    /// will add "auto" entries that start disabled until the user confirms them.
    /// </summary>
    [JsonPropertyName("source")] public string Source { get; set; } = "manual";
}

public sealed class DictionaryDocument
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("entries")] public List<DictionaryEntry> Entries { get; set; } = [];
    [JsonPropertyName("suggestions")] public List<DictionarySuggestion> Suggestions { get; set; } = [];
    /// <summary>Normalized "from→to" keys the user rejected; never suggested again.</summary>
    [JsonPropertyName("rejected")] public List<string> Rejected { get; set; } = [];
    [JsonPropertyName("learning")] public LearningSettings Learning { get; set; } = new();
}

/// <summary>A correction observed after paste, waiting for Accept/Reject. Never applied by itself.</summary>
public sealed class DictionarySuggestion
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("from")] public string From { get; set; } = string.Empty;
    [JsonPropertyName("to")] public string To { get; set; } = string.Empty;
    [JsonPropertyName("seen")] public int Seen { get; set; } = 1;
    [JsonPropertyName("last_seen")] public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("app")] public string? App { get; set; }
}

public sealed class LearningSettings
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("watch_seconds")] public int WatchSeconds { get; set; } = 45;
    /// <summary>Process names (without .exe), case-insensitive.</summary>
    [JsonPropertyName("excluded_apps")] public List<string> ExcludedApps { get; set; } = [];

    public const int MinSeconds = 10;
    public const int MaxSeconds = 120;

    public int ClampedSeconds => Math.Clamp(WatchSeconds, MinSeconds, MaxSeconds);

    public bool IsExcluded(string? processName) =>
        !string.IsNullOrEmpty(processName)
        && ExcludedApps.Any(a => string.Equals(
            a.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase),
            processName,
            StringComparison.OrdinalIgnoreCase));
}

public sealed record DictionaryApplyResult(string Text, int Replacements, IReadOnlyList<string> UsedIds);

/// <summary>
/// Whole-word, case-insensitive replacement with case preservation. Longest phrase wins;
/// word boundaries are Unicode letters/digits, so Cyrillic and Latin both work.
/// </summary>
public static class DictionaryMatcher
{
    private const string WordChar = @"[\p{L}\p{N}_]";

    public static string NormalizeKey(string phrase) =>
        string.Join(' ', SplitWords(phrase)).ToLower(CultureInfo.InvariantCulture);

    private static string[] SplitWords(string phrase) =>
        phrase.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static DictionaryApplyResult Apply(string text, IEnumerable<DictionaryEntry> entries)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new DictionaryApplyResult(text, 0, []);
        }

        var map = new Dictionary<string, DictionaryEntry>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            if (!e.Enabled || string.IsNullOrWhiteSpace(e.From))
            {
                continue;
            }

            var key = NormalizeKey(e.From);
            map.TryAdd(key, e); // first wins on duplicates
        }

        if (map.Count == 0)
        {
            return new DictionaryApplyResult(text, 0, []);
        }

        var alternatives = map.Keys
            .OrderByDescending(k => k.Length)
            .ThenBy(k => k, StringComparer.Ordinal)
            .Select(k => string.Join(@"\s+", k.Split(' ').Select(Regex.Escape)));
        var pattern = $"(?<!{WordChar})(?:{string.Join('|', alternatives)})(?!{WordChar})";
        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var count = 0;
        var used = new List<string>();
        var result = regex.Replace(text, m =>
        {
            if (!map.TryGetValue(NormalizeKey(m.Value), out var entry))
            {
                return m.Value;
            }

            count++;
            if (!used.Contains(entry.Id))
            {
                used.Add(entry.Id);
            }

            return MatchCase(m.Value, entry.To);
        });

        return new DictionaryApplyResult(result, count, used);
    }

    /// <summary>ALL CAPS stays all caps; a capitalized match capitalizes the replacement's first letter.</summary>
    public static string MatchCase(string original, string replacement)
    {
        if (replacement.Length == 0)
        {
            return replacement;
        }

        var letters = original.Where(char.IsLetter).ToArray();
        if (letters.Length > 1 && letters.All(char.IsUpper))
        {
            return replacement.ToUpper(CultureInfo.CurrentCulture);
        }

        var first = original.FirstOrDefault(char.IsLetter);
        if (first != default && char.IsUpper(first))
        {
            var idx = 0;
            while (idx < replacement.Length && !char.IsLetter(replacement[idx]))
            {
                idx++;
            }

            if (idx < replacement.Length && char.IsLower(replacement[idx]))
            {
                var sb = new StringBuilder(replacement);
                sb[idx] = char.ToUpper(replacement[idx], CultureInfo.CurrentCulture);
                return sb.ToString();
            }
        }

        return replacement;
    }
}

/// <summary>Persists the dictionary in its own file next to config.json (not inside it).</summary>
public sealed class DictionaryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _path;
    private DictionaryDocument? _doc;

    public DictionaryStore() : this(AppPaths.DictionaryPath) { }

    public DictionaryStore(string path) => _path = path;

    public event EventHandler? Changed;

    public string FilePath => _path;

    public DictionaryDocument Snapshot()
    {
        lock (_gate)
        {
            return Clone(Load());
        }
    }

    public void Update(Action<DictionaryDocument> mutate)
    {
        lock (_gate)
        {
            mutate(Load());
            SaveLocked();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Applies enabled entries and bumps usage counters. Returns the input unchanged when disabled.</summary>
    public DictionaryApplyResult Apply(string text)
    {
        DictionaryApplyResult result;
        lock (_gate)
        {
            var doc = Load();
            if (!doc.Enabled || doc.Entries.Count == 0)
            {
                return new DictionaryApplyResult(text, 0, []);
            }

            result = DictionaryMatcher.Apply(text, doc.Entries);
            if (result.Replacements == 0)
            {
                return result;
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var e in doc.Entries.Where(e => result.UsedIds.Contains(e.Id)))
            {
                e.Uses++;
                e.LastUsed = now;
            }

            try
            {
                SaveLocked();
            }
            catch (IOException)
            {
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public static string SuggestionKey(string from, string to) =>
        DictionaryMatcher.NormalizeKey(from) + "\u2192" + DictionaryMatcher.NormalizeKey(to);

    /// <summary>Records observed corrections as suggestions. Returns how many were new or bumped.</summary>
    public int AddSuggestions(IEnumerable<CorrectionCandidate> candidates, string? app = null)
    {
        var list = candidates.ToList();
        if (list.Count == 0)
        {
            return 0;
        }

        var touched = 0;
        Update(doc =>
        {
            foreach (var c in list)
            {
                var key = SuggestionKey(c.From, c.To);
                var fromKey = DictionaryMatcher.NormalizeKey(c.From);
                if (doc.Rejected.Contains(key)
                    || doc.Entries.Any(e => DictionaryMatcher.NormalizeKey(e.From) == fromKey))
                {
                    continue;
                }

                var existing = doc.Suggestions.FirstOrDefault(s => SuggestionKey(s.From, s.To) == key);
                if (existing is null)
                {
                    doc.Suggestions.Add(new DictionarySuggestion { From = c.From, To = c.To, App = app });
                }
                else
                {
                    existing.Seen++;
                    existing.LastSeen = DateTimeOffset.UtcNow;
                    existing.App = app ?? existing.App;
                }

                touched++;
            }
        });
        return touched;
    }

    public void AcceptSuggestion(string id) => Update(doc =>
    {
        var s = doc.Suggestions.FirstOrDefault(x => x.Id == id);
        if (s is null)
        {
            return;
        }

        doc.Suggestions.Remove(s);
        var fromKey = DictionaryMatcher.NormalizeKey(s.From);
        doc.Entries.RemoveAll(e => DictionaryMatcher.NormalizeKey(e.From) == fromKey);
        doc.Entries.Add(new DictionaryEntry { From = s.From, To = s.To, Source = "auto" });
    });

    public void RejectSuggestion(string id) => Update(doc =>
    {
        var s = doc.Suggestions.FirstOrDefault(x => x.Id == id);
        if (s is null)
        {
            return;
        }

        doc.Suggestions.Remove(s);
        var key = SuggestionKey(s.From, s.To);
        if (!doc.Rejected.Contains(key))
        {
            doc.Rejected.Add(key);
        }
    });

    public string ExportJson()
    {
        lock (_gate)
        {
            return JsonSerializer.Serialize(Load(), JsonOptions);
        }
    }

    /// <summary>Merges entries from JSON; an existing "from" phrase is overwritten. Returns imported count.</summary>
    public int ImportJson(string json)
    {
        var incoming = JsonSerializer.Deserialize<DictionaryDocument>(json)
            ?? throw new InvalidDataException("Empty dictionary file");
        var imported = 0;
        Update(doc =>
        {
            foreach (var e in incoming.Entries)
            {
                if (string.IsNullOrWhiteSpace(e.From))
                {
                    continue;
                }

                var key = DictionaryMatcher.NormalizeKey(e.From);
                var existing = doc.Entries.FirstOrDefault(x => DictionaryMatcher.NormalizeKey(x.From) == key);
                if (existing is not null)
                {
                    existing.To = e.To;
                    existing.Enabled = e.Enabled;
                }
                else
                {
                    doc.Entries.Add(new DictionaryEntry
                    {
                        From = e.From.Trim(),
                        To = e.To,
                        Enabled = e.Enabled,
                        Uses = e.Uses,
                        LastUsed = e.LastUsed,
                        Source = "import",
                    });
                }

                imported++;
            }
        });
        return imported;
    }

    private DictionaryDocument Load()
    {
        if (_doc is not null)
        {
            return _doc;
        }

        try
        {
            _doc = File.Exists(_path)
                ? JsonSerializer.Deserialize<DictionaryDocument>(File.ReadAllText(_path)) ?? new DictionaryDocument()
                : new DictionaryDocument();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _doc = new DictionaryDocument();
        }

        return _doc;
    }

    private void SaveLocked()
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_doc, JsonOptions));
        File.Move(tmp, _path, overwrite: true);
    }

    private static DictionaryDocument Clone(DictionaryDocument d) =>
        JsonSerializer.Deserialize<DictionaryDocument>(JsonSerializer.Serialize(d))!;
}
