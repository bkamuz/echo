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

    public int CountEntries() => CountEntries(AppPaths.HistoryPath);

    public IReadOnlyList<HistoryEntry> ReadPage(int skipFromNewest, int take) =>
        ReadPage(AppPaths.HistoryPath, skipFromNewest, take);

    internal static int CountEntries(string historyPath)
    {
        if (!File.Exists(historyPath))
        {
            return 0;
        }

        var count = 0;
        foreach (var line in File.ReadLines(historyPath))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                count++;
            }
        }

        return count;
    }

    internal static IReadOnlyList<HistoryEntry> ReadPage(string historyPath, int skipFromNewest, int take)
    {
        if (!File.Exists(historyPath) || take <= 0)
        {
            return [];
        }

        var window = skipFromNewest + take;
        var buffer = new Queue<string>(window);

        foreach (var line in File.ReadLines(historyPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            buffer.Enqueue(line);
            while (buffer.Count > window)
            {
                buffer.Dequeue();
            }
        }

        if (buffer.Count == 0)
        {
            return [];
        }

        var lines = buffer.ToArray();
        var end = lines.Length - skipFromNewest;
        if (end <= 0)
        {
            return [];
        }

        var start = Math.Max(0, end - take);
        var result = new List<HistoryEntry>(end - start);
        for (var i = end - 1; i >= start; i--)
        {
            if (TryParseLine(lines[i], out var entry))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    internal static bool TryParseLine(string line, out HistoryEntry entry)
    {
        entry = default!;
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

            entry = new HistoryEntry(timestamp, engine, text);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
