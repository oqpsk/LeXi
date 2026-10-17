using System.Text.Json;

namespace Lexi.Core;

public sealed record SpecialistReference(string Word, string[] Keys, string Domain, string English, string Chinese,
    string SourceName, string SourceUrl, string Expansion)
{
    public Sense Evidence => new("noun, " + Domain + (Expansion.Length > 0 ? ", abbreviation: " + Expansion : ""),
        Chinese + " " + English, [], SourceName, SourceUrl, Word);
}

// A small, reviewed seed collection, not an exhaustive technical dictionary.
// New terms are still covered by model knowledge and the ordinary online sources.
public static class SpecialistReferences
{
    private static readonly Lazy<SpecialistReference[]> Entries = new(() =>
    {
        using var stream = typeof(SpecialistReferences).Assembly.GetManifestResourceStream("Lexi.Core.specialist-references.json")!;
        return JsonSerializer.Deserialize<SpecialistReference[]>(stream, Json.Options) ?? [];
    });
    public static bool Covered(SpecialistReference brief, SenseGroup[] groups, OnlineEntry? evidence)
    {
        if (evidence == null) return false;
        var numbers = evidence.Senses.Select((s,i) => (s,i)).Where(x =>
            x.s.SourceUrl == brief.SourceUrl && x.s.Definition == brief.Evidence.Definition).Select(x => x.i + 1).ToHashSet();
        return groups.Any(g => g.Variants.Any(v => !v.Provisional &&
            v.Sense.Evidence.Any(numbers.Contains) && LexicalCoverage.Specialist(v.Sense.Register) &&
            (brief.Expansion.Length == 0 ? !LexicalCoverage.Abbreviation(v.Sense) :
                Queries.Normalize(v.Sense.Expansion) == Queries.Normalize(brief.Expansion))));
    }
    public static SpecialistReference[] Uncovered(string query, SenseGroup[] groups, OnlineEntry? evidence) =>
        Lookup(query).Where(b => !Covered(b, groups, evidence)).ToArray();
    public static SpecialistReference[] Cited(Sense sense) => Entries.Value.Where(b =>
        b.SourceUrl == sense.SourceUrl && b.Evidence.Definition == sense.Definition).ToArray();
    public static SpecialistReference[] Lookup(string query) => Entries.Value.Where(e =>
        e.Keys.Any(k => Queries.Normalize(k) == Queries.Normalize(query))).ToArray();
}
