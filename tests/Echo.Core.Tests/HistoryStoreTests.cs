using echo.Core;

namespace echo.Core.Tests;

public class HistoryStoreTests
{
    [Fact]
    public void ReadPage_ReturnsNewestEntriesFirst()
    {
        var path = CreateHistoryFile(
            """{"ts":"2026-01-01T00:00:00Z","engine":"GigaAM","text":"first"}""",
            """{"ts":"2026-01-01T00:00:01Z","engine":"GigaAM","text":"second"}""",
            """{"ts":"2026-01-01T00:00:02Z","engine":"Whisper","text":"third"}""");

        try
        {
            var page = HistoryStore.ReadPage(path, 0, 2);

            Assert.Equal(2, page.Count);
            Assert.Equal("third", page[0].Text);
            Assert.Equal("second", page[1].Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadPage_SkipsNewestEntriesForOlderPages()
    {
        var lines = Enumerable.Range(1, 5)
            .Select(i => $$"""{"ts":"2026-01-01T00:00:0{{i}}Z","engine":"GigaAM","text":"entry-{{i}}"}""");
        var path = CreateHistoryFile(lines.ToArray());

        try
        {
            var page = HistoryStore.ReadPage(path, 2, 2);

            Assert.Equal(2, page.Count);
            Assert.Equal("entry-3", page[0].Text);
            Assert.Equal("entry-2", page[1].Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CountEntries_IgnoresBlankLines()
    {
        var path = CreateHistoryFile(
            """{"ts":"2026-01-01T00:00:00Z","engine":"GigaAM","text":"one"}""",
            string.Empty,
            """{"ts":"2026-01-01T00:00:01Z","engine":"GigaAM","text":"two"}""");

        try
        {
            Assert.Equal(2, HistoryStore.CountEntries(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateHistoryFile(params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), "echo-history-" + Guid.NewGuid() + ".jsonl");
        File.WriteAllLines(path, lines);
        return path;
    }
}
