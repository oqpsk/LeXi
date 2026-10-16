using System.Text;
using System.Text.Json;

namespace Lexi.Core;

public sealed class OfflineDictionary : IDisposable
{
    private sealed record IndexRow(string Word, long Offset, int Length, int Frequency);
    private readonly IndexRow[] rows;
    private readonly FileStream data;
    private readonly Dictionary<string, string> forms = new(StringComparer.Ordinal);
    public int Count => rows.Length;
    public OfflineDictionary(string folder)
    {
        using var reader = new BinaryReader(File.OpenRead(Path.Combine(folder, "lexicon.idx")), Encoding.UTF8);
        if (reader.ReadString() != "LEXI1") throw new InvalidDataException("词库索引格式不受支持。");
        rows = new IndexRow[reader.ReadInt32()];
        for (var i = 0; i < rows.Length; i++) rows[i] = new(reader.ReadString(), reader.ReadInt64(), reader.ReadInt32(), reader.ReadInt32());
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++) forms.TryAdd(reader.ReadString(), reader.ReadString());
        data = new FileStream(Path.Combine(folder, "lexicon.dat"), FileMode.Open, FileAccess.Read, FileShare.Read);
    }
    private int LowerBound(string query)
    {
        int lo = 0, hi = rows.Length;
        while (lo < hi) { int mid = lo + (hi - lo) / 2; if (string.CompareOrdinal(rows[mid].Word, query) < 0) lo = mid + 1; else hi = mid; }
        return lo;
    }
    private LocalEntry? Exact(string query)
    {
        var i = LowerBound(query);
        if (i >= rows.Length || rows[i].Word != query) return null;
        var row = rows[i];
        var bytes = new byte[row.Length];
        lock (data) { data.Seek(row.Offset, SeekOrigin.Begin); data.ReadExactly(bytes); }
        return JsonSerializer.Deserialize<LocalEntry>(bytes, Json.Options);
    }
    public LocalEntry? Lookup(string query)
    {
        query = Queries.Normalize(query);
        var hit = Exact(query);
        if (hit != null) return hit;
        return forms.TryGetValue(query, out var lemma) ? Exact(lemma) : null;
    }
    public string[] Suggest(string query, int limit = 7)
    {
        query = Queries.Normalize(query);
        if (query.Length == 0) return [];
        var candidates = new List<IndexRow>();
        for (var i = LowerBound(query); i < rows.Length && rows[i].Word.StartsWith(query, StringComparison.Ordinal) && candidates.Count < 1200; i++) candidates.Add(rows[i]);
        return candidates.OrderBy(x => x.Word == query ? 0 : 1).ThenBy(x => x.Frequency > 0 ? x.Frequency : 1000000).ThenBy(x => x.Word.Length).Take(limit).Select(x => x.Word).ToArray();
    }
    public void Dispose() => data.Dispose();
}
