namespace Lexi.Core;

public sealed record LocalHit(InstalledDictionary Dictionary, LocalEntry Entry);

public sealed class OfflineLibrary : IDisposable
{
    private readonly List<(InstalledDictionary Info, OfflineDictionary Data)> loaded = [];
    public InstalledDictionary[] Dictionaries { get; }
    public int Count => loaded.Sum(d => d.Data.Count);
    public OfflineLibrary(string root)
    {
        var catalog = DictionaryCatalog.InspectAll(root);
        for (int i = 0; i < catalog.Length; i++)
        {
            if (!catalog[i].Available) continue;
            try { loaded.Add((catalog[i], new OfflineDictionary(catalog[i].Folder))); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            { catalog[i] = catalog[i] with { Available = false, Status = "词库无法读取" }; }
        }
        Dictionaries = catalog;
    }
    private IEnumerable<(InstalledDictionary Info, OfflineDictionary Data)> Enabled(AppSettings settings) =>
        loaded.Where(d => settings.OfflineEnabled && !(settings.DisabledDictionaries ?? []).Contains(d.Info.Id));
    public LocalHit[] Lookup(string query, AppSettings settings)
    {
        var traditional = Queries.TraditionalKey(query);
        return Enabled(settings).Select(d => (d.Info, Entry: d.Data.Lookup(query) ?? (traditional != query ? d.Data.Lookup(traditional) : null)))
            .Where(d => d.Entry != null).Select(d => new LocalHit(d.Info, d.Entry!)).ToArray();
    }
    public string[] Suggest(string query, AppSettings settings) => Queries.IsText(query) ? [] :
        Enabled(settings).SelectMany(d => d.Data.Suggest(query)).Distinct().Take(7).ToArray();
    public WordFormSet[] Forms(string query, AppSettings settings)
    {
        var entries = Enabled(settings).Where(d => d.Info.Id == "ecdict").SelectMany(d =>
            new[] { d.Data.Lookup(query), d.Data.LookupInflectionLemma(query) }.OfType<LocalEntry>().Distinct().Select(e => new LocalHit(d.Info, e))).ToArray();
        return WordForms.Resolve(entries, lemma => Lookup(lemma, settings));
    }
    public void Dispose() { foreach (var dictionary in loaded) dictionary.Data.Dispose(); }
}

public static class DictionaryEvidence
{
    public static OnlineEntry? Combine(string query, IEnumerable<LocalHit> local, OnlineEntry? online, EtymologyExcerpt[]? etymologies = null)
    {
        var senses = new List<Sense>(SpecialistReferences.Lookup(query).Select(r => r.Evidence));
        if (online != null) senses.AddRange(online.Senses.OrderBy(s => LexicalCoverage.EvidenceRank(s.PartOfSpeech + " " + s.Definition))
            .Take(12).Select(s => s with { SourceName = "Wiktionary", SourceUrl = s.SourceUrl.Length > 0 ? s.SourceUrl : online.Url }));
        senses.AddRange(local.Where(h => h.Dictionary.AllowAiEvidence).Take(8).Select(h => new Sense(h.Dictionary.Id.StartsWith("naer-") ? "terminology pair, not a full definition" : "",
            Excerpt(string.Join("\n", new[] { h.Entry.English, h.Entry.Chinese }.Where(s => !string.IsNullOrWhiteSpace(s)))),
            [], h.Dictionary.Name, h.Dictionary.SourceUrl)));
        return senses.Count == 0 && etymologies is not { Length: > 0 } ? null : new(query, senses.Take(20).ToArray(), "", online?.FetchedAt ?? DateTimeOffset.UtcNow, etymologies);
    }
    private static string Excerpt(string text) => text.Length > 4000 ? text[..4000] + " [excerpt truncated]" : text;
}
