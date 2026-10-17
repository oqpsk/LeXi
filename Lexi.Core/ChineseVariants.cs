using System.Text;

namespace Lexi.Core;

// Character-level OpenCC tables, embedded so every platform derives identical lookup keys.
// Candidates keep OpenCC's order; displayed dictionary text is never converted.
public static class ChineseVariants
{
    private static readonly Lazy<Dictionary<int, string[]>> ToTraditional = new(() => Load("opencc-st-characters.txt"));
    private static readonly Lazy<Dictionary<int, string[]>> ToSimplified = new(() => Load("opencc-ts-characters.txt"));
    public const int MaxCandidates = 16;

    public static string Simplified(string text) => Convert(text, ToSimplified.Value);
    public static string Traditional(string text) => Convert(text, ToTraditional.Value);

    // A simplified character may have several traditional forms (钟 → 鍾 / 鐘). Older indexes
    // without simplified aliases need each combination, bounded for long phrases.
    public static string[] TraditionalCandidates(string text, int limit = MaxCandidates)
    {
        var results = new List<StringBuilder> { new() };
        foreach (var rune in text.EnumerateRunes())
        {
            var options = ToTraditional.Value.TryGetValue(rune.Value, out var mapped) ? mapped : [rune.ToString()];
            var next = new List<StringBuilder>();
            foreach (var prefix in results)
                foreach (var option in options)
                {
                    if (next.Count >= limit) break;
                    next.Add(new StringBuilder(prefix.ToString()).Append(option));
                }
            results = next;
        }
        return results.Select(r => r.ToString()).Distinct().ToArray();
    }

    private static string Convert(string text, Dictionary<int, string[]> table)
    {
        var output = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
            output.Append(table.TryGetValue(rune.Value, out var mapped) ? mapped[0] : rune.ToString());
        return output.ToString();
    }

    private static Dictionary<int, string[]> Load(string name)
    {
        using var stream = typeof(ChineseVariants).Assembly.GetManifestResourceStream("Lexi.Core.ChineseVariants." + name)
            ?? throw new InvalidDataException("Missing Chinese variant table: " + name);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var table = new Dictionary<int, string[]>();
        while (reader.ReadLine() is { } line)
        {
            var columns = line.Split('\t');
            if (columns.Length != 2 || !Rune.TryGetRuneAt(columns[0], 0, out var source) || source.Utf16SequenceLength != columns[0].Length) continue;
            var targets = columns[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (targets.Length > 0) table.TryAdd(source.Value, targets);
        }
        return table;
    }
}
