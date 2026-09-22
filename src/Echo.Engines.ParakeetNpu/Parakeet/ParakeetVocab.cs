namespace echo.Engines.ParakeetNpu.Parakeet;

/// <summary>
/// Custom vocab loader for the istupakov Parakeet release.
/// </summary>
public sealed class ParakeetVocab
{
    private const char WordBoundaryMarker = '\u2581';
    private readonly Dictionary<int, string> _table;

    public int BlankId { get; }
    public int Size => _table.Count;

    private ParakeetVocab(Dictionary<int, string> table, int blankId)
    {
        _table = table;
        BlankId = blankId;
    }

    public static ParakeetVocab Load(string path)
    {
        var text = File.ReadAllText(path);
        var table = new Dictionary<int, string>();
        var blankId = -1;

        foreach (var rawLine in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var splitIndex = line.LastIndexOf(' ');
            if (splitIndex <= 0 || splitIndex >= line.Length - 1)
            {
                continue;
            }

            var token = line[..splitIndex];
            if (!int.TryParse(line[(splitIndex + 1)..], out var id))
            {
                throw new InvalidOperationException($"Vocab id parse failed: {line}");
            }

            var decoded = token.Replace(WordBoundaryMarker, ' ');
            if (token == "<blk>")
            {
                blankId = id;
            }

            table[id] = decoded;
        }

        if (blankId < 0)
        {
            throw new InvalidOperationException("Vocab is missing <blk> token.");
        }

        return new ParakeetVocab(table, blankId);
    }

    public string Detokenize(IReadOnlyList<int> ids)
    {
        var builder = new System.Text.StringBuilder(ids.Count * 4);
        foreach (var id in ids)
        {
            if (_table.TryGetValue(id, out var token))
            {
                builder.Append(token);
            }
        }

        return builder.ToString().TrimStart();
    }
}
