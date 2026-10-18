namespace Lexi.Core;

public sealed record QualityCase(string Id, string Query, string[] RequiredMeanings, string CoreTerm = "", string[]? ExcludedCoreTags = null);
public sealed record QualityFinding(string Rule, bool Passed);

// Repeatable lexical checks, not a truth verifier. Historical/factual accuracy and example naturalness
// require editorial review. These diagnostics never promote model content to sourced evidence.
public static class DefinitionQuality
{
    public static QualityFinding[] Evaluate(QualityCase item, AiEntry entry)
    {
        var core = entry.Senses.Where(s => s.CoreMeaning == true).ToArray();
        var all = string.Join("\n", entry.Senses.Select(s => s.Chinese + " " + s.English + " " + s.Expansion));
        var findings = new List<QualityFinding>
        {
            new("query-identity", entry.Word == item.Query),
            new("known-meaning", entry.Found && entry.Senses.Length > 0),
            new("centrality", core.Length is >= 1 and <= 2),
            new("translation-cue", entry.Senses.All(s => s.ChineseTerm.Length == 0 ||
                s.Chinese.Contains(s.ChineseTerm, StringComparison.Ordinal) && s.Gloss.Contains(s.ChineseTerm, StringComparison.Ordinal))),
            new("abbreviation-identity", entry.Senses.All(s => s.LexicalForm == "abbreviation" ? !string.IsNullOrWhiteSpace(s.Expansion) : string.IsNullOrWhiteSpace(s.Expansion))),
            new("bilingual-completeness", entry.Senses.All(s => s.Chinese.Length > 0 && s.English.Length > 0 && s.Example.Length > 0 && s.ExampleChinese.Length > 0)),
            new("cue-bounds", entry.Senses.All(s => s.Gloss.Length is > 0 and <= 32)),
            new("core-name", item.CoreTerm.Length == 0 || core.Any(s => s.ChineseTerm.Contains(item.CoreTerm, StringComparison.Ordinal) && s.Gloss.Contains(item.CoreTerm, StringComparison.Ordinal))),
            new("native-field", core.All(s => !(item.ExcludedCoreTags ?? []).Any(t => s.Register.Contains(t, StringComparison.OrdinalIgnoreCase))))
        };
        findings.AddRange(item.RequiredMeanings.Select((term, i) => new QualityFinding("coverage-" + (i + 1), all.Contains(term, StringComparison.OrdinalIgnoreCase))));
        return findings.ToArray();
    }
}
