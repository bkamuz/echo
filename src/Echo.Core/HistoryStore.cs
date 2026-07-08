using System.Text.Json;
using echo.Abstractions.Core;

namespace echo.Core;

public sealed record HistoryEntry(DateTimeOffset Timestamp, string Engine, string Text);

public sealed class HistoryStore
{
    public void Append(string engine, string text)
    {
        AppPaths.EnsureDirectories();
        var entry = new
        {
            ts = DateTimeOffset.UtcNow.ToString("O"),
            engine,
            text,
        };
        var line = JsonSerializer.Serialize(entry);
        File.AppendAllText(AppPaths.HistoryPath, line + Environment.NewLine);
    }

    public IReadOnlyList<HistoryEntry> ReadAll(int limit = 500)
    {
        if (!File.Exists(AppPaths.HistoryPath))
        {
            return [];
        }

        var entries = new List<HistoryEntry>();
        var pending = new Stack<string>();

        foreach (var line in File.ReadLines(AppPaths.HistoryPath))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                pending.Push(line);
            }
        }

        foreach (var line in pending.Take(limit))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var ts = root.GetProperty("ts").GetString() ?? DateTimeOffset.UtcNow.ToString("O");
                var engine = root.GetProperty("engine").GetString() ?? string.Empty;
                var text = root.GetProperty("text").GetString() ?? string.Empty;
                if (!DateTimeOffset.TryParse(ts, out var timestamp))
                {
                    timestamp = DateTimeOffset.UtcNow;
                }

                entries.Add(new HistoryEntry(timestamp, engine, text));
            }
            catch
            {
            }
        }

        return entries;
    }
}
