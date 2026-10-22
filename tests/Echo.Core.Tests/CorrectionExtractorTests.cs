using echo.Core;

namespace echo.Core.Tests;

public class CorrectionExtractorTests
{
    [Fact]
    public void SingleWordReplacement()
    {
        var c = Assert.Single(CorrectionExtractor.Extract("открой паркет в настройках", "открой Parakeet в настройках"));
        Assert.Equal(("паркет", "Parakeet"), (c.From, c.To));
    }

    [Fact]
    public void MultiWordToSingleWord()
    {
        var c = Assert.Single(CorrectionExtractor.Extract("залей на гит хаб сегодня", "залей на GitHub сегодня"));
        Assert.Equal(("гит хаб", "GitHub"), (c.From, c.To));
    }

    [Theory]
    [InlineData("привет мир", "Привет, мир!")]          // case + punctuation only
    [InlineData("привет мир", "привет большой мир")]    // pure insertion
    [InlineData("привет большой мир", "привет мир")]    // pure deletion
    [InlineData("сегодня хорошая погода на улице", "завтра пойду в кино с друзьями")] // rewrite
    [InlineData("ёлка стоит", "елка стоит")]             // ё/е only
    public void IgnoredEdits(string injected, string edited) =>
        Assert.Empty(CorrectionExtractor.Extract(injected, edited));

    [Fact]
    public void LongReplacementIgnored() =>
        Assert.Empty(CorrectionExtractor.Extract(
            "раз два три четыре пять шесть семь восемь девять десять",
            "раз один два три четыре вообще другое слово пять шесть семь восемь девять десять"));

    [Fact]
    public void TwoSeparateFixes()
    {
        var r = CorrectionExtractor.Extract("запусти эхо и открой паркет потом", "запусти Echo и открой Parakeet потом");
        Assert.Equal([new("эхо", "Echo"), new("паркет", "Parakeet")], r);
    }

    [Fact]
    public void SpanTracker_ReturnsOnlyInjectedSpan()
    {
        var t = InjectedSpanTracker.Create("Секрет: 1234. открой паркет\nподпись", "открой паркет ");
        Assert.NotNull(t);
        Assert.Equal("открой Parakeet", t!.CurrentSpan("Секрет: 1234. открой Parakeet\nподпись"));
        Assert.Null(t.CurrentSpan("Другое. открой Parakeet\nподпись"));
        Assert.Null(t.CurrentSpan(""));
    }

    [Fact]
    public void Store_SuggestionsCountRejectAndAccept()
    {
        var dir = Path.Combine(Path.GetTempPath(), "echo-learn-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DictionaryStore(Path.Combine(dir, "d.json"));
            store.AddSuggestions([new("паркет", "Parakeet")], "chrome");
            store.AddSuggestions([new("Паркет", "Parakeet"), new("эхо", "Echo")], "code");
            var doc = store.Snapshot();
            Assert.Equal(2, doc.Suggestions.Count);
            Assert.Equal(2, doc.Suggestions.Single(s => s.To == "Parakeet").Seen);
            Assert.Empty(doc.Entries); // nothing auto-promoted
            Assert.Equal("паркет", store.Apply("паркет").Text);

            store.RejectSuggestion(doc.Suggestions.Single(s => s.To == "Echo").Id);
            store.AddSuggestions([new("эхо", "Echo")]);
            Assert.DoesNotContain(store.Snapshot().Suggestions, s => s.To == "Echo");

            store.AcceptSuggestion(doc.Suggestions.Single(s => s.To == "Parakeet").Id);
            doc = store.Snapshot();
            Assert.Empty(doc.Suggestions);
            Assert.Equal("auto", Assert.Single(doc.Entries).Source);
            Assert.Equal("Parakeet", store.Apply("паркет").Text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LearningSettings_Exclusion()
    {
        var s = new LearningSettings { ExcludedApps = ["Telegram.exe", " keepass "] };
        Assert.True(s.IsExcluded("telegram"));
        Assert.True(s.IsExcluded("KeePass"));
        Assert.False(s.IsExcluded("chrome"));
    }

    [Fact]
    public void ThreeSeparateFixes_ThreeSuggestions()
    {
        var r = CorrectionExtractor.Extract(
            "открой паркет и запусти эхо потом залей на гид",
            "открой Parakeet и запусти Echo потом залей на GitHub");
        Assert.Equal(
            [("паркет", "Parakeet"), ("эхо", "Echo"), ("гид", "GitHub")],
            r.Select(c => (c.From, c.To)).ToArray());
    }

    [Fact]
    public void ManyFixesInShortText_AllKept()
    {
        var r = CorrectionExtractor.Extract("паркет и эхо и гид и квин и вин", "Parakeet и Echo и GitHub и Qwen и Win");
        Assert.Equal(5, r.Count);
    }

    [Fact]
    public void AdjacentMultiWordFix_OnePhrase()
    {
        var c = Assert.Single(CorrectionExtractor.Extract("я открыл вижу ал студио вчера", "я открыл Visual Studio вчера"));
        Assert.Equal(("вижу ал студио", "Visual Studio"), (c.From, c.To));
    }

    [Fact]
    public void DuplicateFix_Deduped()
    {
        var c = Assert.Single(CorrectionExtractor.Extract("паркет лучше чем паркет", "Parakeet лучше чем Parakeet"));
        Assert.Equal(("паркет", "Parakeet"), (c.From, c.To));
    }

    [Fact]
    public void Store_KeepsEarlierSuggestions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"echo-dict-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DictionaryStore(path);
            store.AddSuggestions(CorrectionExtractor.Extract("открой паркет", "открой Parakeet"));
            store.AddSuggestions(CorrectionExtractor.Extract("запусти эхо и гид", "запусти Echo и GitHub"));
            Assert.Equal(3, store.Snapshot().Suggestions.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AdjacentTypoFixes_Separate()
    {
        var r = CorrectionExtractor.Extract("the qick brwn fox", "the quick brown fox");
        Assert.Equal([("qick", "quick"), ("brwn", "brown")], r.Select(c => (c.From, c.To)).ToArray());
    }
}
