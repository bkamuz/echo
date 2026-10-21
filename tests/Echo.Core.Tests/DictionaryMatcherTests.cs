using echo.Core;

namespace echo.Core.Tests;

public class DictionaryMatcherTests
{
    private static DictionaryEntry E(string from, string to, bool enabled = true) => new() { From = from, To = to, Enabled = enabled };

    [Theory]
    [InlineData("гит хаб открыт", "GitHub открыт")]
    [InlineData("Гит хаб открыт", "GitHub открыт")]
    [InlineData("смотри   гит\tхаб", "смотри   GitHub")]
    public void MultiWordPhrase_CaseInsensitive_FlexibleWhitespace(string input, string expected) =>
        Assert.Equal(expected, DictionaryMatcher.Apply(input, [E("гит хаб", "GitHub")]).Text);

    [Fact]
    public void CapitalizedMatch_CapitalizesLowercaseReplacement()
    {
        var r = DictionaryMatcher.Apply("Паркет работает. А паркет нет.", [E("паркет", "parakeet")]);
        Assert.Equal("Parakeet работает. А parakeet нет.", r.Text);
        Assert.Equal(2, r.Replacements);
    }

    [Fact]
    public void AllCapsMatch_UppercasesReplacement() =>
        Assert.Equal("ЭХО", DictionaryMatcher.Apply("ЕХО", [E("ехо", "эхо")]).Text);

    [Theory]
    [InlineData("котлета", "котлета")]          // Cyrillic: no match inside word
    [InlineData("кот, котик", "кошка, котик")]
    [InlineData("cats cat", "cats dog")]       // Latin boundary
    [InlineData("cat-кот", "dog-кошка")]       // punctuation is a boundary
    public void WholeWordOnly(string input, string expected) =>
        Assert.Equal(expected, DictionaryMatcher.Apply(input, [E("кот", "кошка"), E("cat", "dog")]).Text);

    [Fact]
    public void LongestMatchWins()
    {
        var r = DictionaryMatcher.Apply("эхо апп и эхо", [E("эхо", "Echo"), E("эхо апп", "Echo.App")]);
        Assert.Equal("Echo.App и Echo", r.Text);
        Assert.Equal(2, r.UsedIds.Count);
    }

    [Fact]
    public void DisabledEntry_Ignored() =>
        Assert.Equal("кот", DictionaryMatcher.Apply("кот", [E("кот", "кошка", enabled: false)]).Text);

    [Fact]
    public void RegexCharactersAreLiteral() =>
        Assert.Equal("C# ok", DictionaryMatcher.Apply("си шарп ok", [E("си шарп", "C#"), E("(", "x")]).Text);

    [Fact]
    public void Store_AppliesCountsAndPersists_SeparateFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "echo-dict-" + Guid.NewGuid().ToString("N"), "dictionary.json");
        try
        {
            var store = new DictionaryStore(path);
            store.Update(d => d.Entries.Add(E("кот", "кошка")));
            var r = store.Apply("кот и кот");
            Assert.Equal("кошка и кошка", r.Text);

            var reloaded = new DictionaryStore(path).Snapshot();
            Assert.Equal(1, reloaded.Entries[0].Uses);
            Assert.NotNull(reloaded.Entries[0].LastUsed);

            store.Update(d => d.Enabled = false);
            Assert.Equal("кот", store.Apply("кот").Text);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Store_ExportImport_RoundTripMergesByPhrase()
    {
        var dir = Path.Combine(Path.GetTempPath(), "echo-dict-" + Guid.NewGuid().ToString("N"));
        try
        {
            var a = new DictionaryStore(Path.Combine(dir, "a.json"));
            a.Update(d => { d.Entries.Add(E("кот", "кошка")); d.Entries.Add(E("пёс", "собака")); });
            var b = new DictionaryStore(Path.Combine(dir, "b.json"));
            b.Update(d => d.Entries.Add(E("КОТ", "кисa")));

            Assert.Equal(2, b.ImportJson(a.ExportJson()));
            var doc = b.Snapshot();
            Assert.Equal(2, doc.Entries.Count);
            Assert.Equal("кошка", doc.Entries.Single(e => e.From == "КОТ").To);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
