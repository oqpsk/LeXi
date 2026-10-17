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
    public static SpecialistReference[] Lookup(string query) => Entries.Value.Where(e =>
        e.Keys.Any(k => Queries.Normalize(k) == Queries.Normalize(query))).ToArray();
}
