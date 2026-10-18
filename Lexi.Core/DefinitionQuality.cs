namespace Lexi.Core;

public sealed record QualityCase(string Id, string Query, string[] RequiredMeanings, string CoreTerm = "", string[]? ExcludedCoreTags = null);
public sealed record QualityFinding(string Rule, bool Passed);

// Repeatable lexical checks, not a truth verifier. Historical/factual accuracy and example naturalness
// require editorial review. These diagnostics never promote model content to sourced evidence.
public static class DefinitionQuality
{
    private static bool Contains(string text, string term) => string.Concat(text.Where(c => !char.IsWhiteSpace(c))).Contains(string.Concat(term.Where(c => !char.IsWhiteSpace(c))), StringComparison.OrdinalIgnoreCase);
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
                Contains(s.Chinese, s.ChineseTerm) && Contains(s.Gloss, s.ChineseTerm))),
            new("abbreviation-identity", entry.Senses.All(s => s.LexicalForm == "abbreviation" ? !string.IsNullOrWhiteSpace(s.Expansion) : string.IsNullOrWhiteSpace(s.Expansion))),
            new("bilingual-completeness", entry.Senses.All(s => Queries.HasChinese(s.Chinese) && s.English.Any(char.IsAsciiLetter) && s.Example.Any(char.IsAsciiLetter) && Queries.HasChinese(s.ExampleChinese))),
            new("evidence-status", entry.EvidenceStatus is "source_based" or "unverified" or "unknown"),
            new("evidence-consistency", entry.Senses.All(s => s.Evidence.All(n => n > 0)) && (entry.EvidenceStatus != "source_based" || entry.Senses.All(s => s.Evidence.Length > 0))),
            new("cue-bounds", entry.Senses.All(s => s.Gloss.Length is > 0 and <= 32)),
            new("core-name", item.CoreTerm.Length == 0 || core.Any(s => Contains(s.ChineseTerm, item.CoreTerm) && Contains(s.Gloss, item.CoreTerm))),
            new("native-field", core.All(s => !(item.ExcludedCoreTags ?? []).Any(t => s.Register.Contains(t, StringComparison.OrdinalIgnoreCase))))
        };
        findings.AddRange(item.RequiredMeanings.Select((term, i) => new QualityFinding("coverage-" + (i + 1), all.Contains(term, StringComparison.OrdinalIgnoreCase))));
        return findings.ToArray();
    }
}
