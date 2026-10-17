namespace Lexi.Core;

public sealed record WordForm(string Kind, string Label, string[] Words, string SourceName = "", string SourceUrl = "", string Note = "");
public sealed record WordFormSet(string Lemma, WordForm[] Forms, string SourceName, string SourceUrl)
{
    public string[] Roles(string query) => Forms.Where(f => f.Words.Any(w => Queries.Normalize(w) == Queries.Normalize(query))).Select(f => f.Label).ToArray();
}

public static partial class WordForms
{
    private static readonly (string Key, string Label)[] Types =
        [("p", "过去式"), ("d", "过去分词"), ("i", "现在分词"), ("3", "第三人称单数"), ("s", "复数"), ("r", "比较级"), ("t", "最高级")];
    public static WordFormSet[] Combine(params WordFormSet[][] sources) => sources.SelectMany(s => s)
        .GroupBy(s => Queries.Normalize(s.Lemma)).Select(group => new WordFormSet(group.First().Lemma,
            group.SelectMany(s => s.Forms.Select(f => f with { SourceName = f.SourceName.Length > 0 ? f.SourceName : s.SourceName,
                SourceUrl = f.SourceUrl.Length > 0 ? f.SourceUrl : s.SourceUrl }))
            .GroupBy(f => f.Kind).OrderBy(g => Array.FindIndex(Types, t => t.Key == g.Key))
            .Select(g => g.First() with { Words = g.SelectMany(f => f.Words).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                SourceName = string.Join(" / ", g.Select(f => f.SourceName).Distinct()),
                SourceUrl = string.Join("\n", g.Select(f => f.SourceUrl).Distinct()) }).ToArray(),
            string.Join(" / ", group.Select(s => s.SourceName).Distinct()),
            string.Join("\n", group.Select(s => s.SourceUrl).Distinct()))).ToArray();
    public static Dictionary<string, string[]> Parse(string exchange) => exchange.Split('/', StringSplitOptions.RemoveEmptyEntries)
        .Select(s => s.Split(':', 2)).Where(p => p.Length == 2)
        .GroupBy(p => p[0]).ToDictionary(g => g.Key, g => g.SelectMany(p => p[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).Distinct().ToArray());

    public static WordFormSet[] Resolve(LocalHit[] hits, Func<string, LocalHit[]> lookup)
    {
        var result = new List<WordFormSet>();
        foreach (var hit in hits.Where(h => h.Dictionary.Id == "ecdict" && h.Entry.Exchange.Length > 0))
        {
            var fields = Parse(hit.Entry.Exchange);
            var lemmas = fields.GetValueOrDefault("0", [hit.Entry.Word]).AsEnumerable();
            if (fields.ContainsKey("0") && new[] { "p", "i", "3" }.Any(k => fields.TryGetValue(k, out var values) && values.Any(v => v != hit.Entry.Word)))
                lemmas = lemmas.Append(hit.Entry.Word);
            foreach (var lemma in lemmas.Take(8))
            {
                var original = Queries.Normalize(lemma) == Queries.Normalize(hit.Entry.Word) ? hit :
                    lookup(lemma).FirstOrDefault(h => h.Dictionary.Id == hit.Dictionary.Id);
                var forms = original == null ? new Dictionary<string, string[]>() : Parse(original.Entry.Exchange);
                var available = Types.Where(t => forms.TryGetValue(t.Key, out var words) && words.Length > 0)
                    .Select(t => new WordForm(t.Key, t.Label, forms[t.Key])).ToList();
                // Retain only relationships explicitly present in the source; never guess suffixes.
                if (available.Count == 0 && fields.TryGetValue("1", out var roles))
                    foreach (var type in Types.Where(t => roles.Any(r => r.Contains(t.Key, StringComparison.Ordinal))))
                        available.Add(new(type.Key, type.Label, [hit.Entry.Word]));
                if (available.Count > 0 && !result.Any(r => Queries.Normalize(r.Lemma) == Queries.Normalize(lemma)))
                    result.Add(new(lemma, available.ToArray(), hit.Dictionary.Name, hit.Dictionary.SourceUrl));
            }
        }
        return result.ToArray();
    }
}
