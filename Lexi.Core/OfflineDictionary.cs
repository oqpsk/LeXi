using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.Json;

namespace Lexi.Core;

public sealed class OfflineDictionary : IDisposable
{
    // The index stays in its file, mapped into memory; only where each row starts is held. A row is
    // the word (length-prefixed UTF-8), then the entry's offset and length in lexicon.dat and its
    // frequency. ECDICT's 770,000 rows take 3 MB this way instead of 80 MB of row objects, and the
    // mapped pages can be dropped and read again, which keeps the app small on a phone.
    private readonly MemoryMappedFile map;
    private readonly MemoryMappedViewAccessor index;
    private readonly int[] rows;
    private readonly FileStream data;
    private readonly Dictionary<string, string> forms = new(StringComparer.Ordinal);
    public int Count => rows.Length;
    public OfflineDictionary(string folder)
    {
        var path = Path.Combine(folder, "lexicon.idx");
        var length = new FileInfo(path).Length;
        if (length == 0) throw new InvalidDataException("词库索引格式不受支持。");
        map = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        try
        {
            index = map.CreateViewAccessor(0, length, MemoryMappedFileAccess.Read);
            long position = 0;
            if (Text(ref position) != "LEXI1") throw new InvalidDataException("词库索引格式不受支持。");
            var count = index.ReadInt32(position); position += 4;
            // A row takes at least 17 bytes; a larger count is a damaged file, not a reason to allocate.
            if (count < 0 || count > length / 17) throw new InvalidDataException("词库索引已损坏。");
            rows = new int[count];
            for (var i = 0; i < count; i++)
            {
                rows[i] = checked((int)position);
                var word = Prefix(ref position);
                position += word + 16;
            }
            var formCount = index.ReadInt32(position); position += 4;
            for (var i = 0; i < formCount; i++) forms.TryAdd(Text(ref position), Text(ref position));
            data = new FileStream(Path.Combine(folder, "lexicon.dat"), FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch
        {
            index?.Dispose(); map.Dispose();
            throw;
        }
    }

    // BinaryWriter's string length: seven bits a byte, low bits first.
    private int Prefix(ref long position)
    {
        int value = 0, shift = 0;
        while (true)
        {
            var b = index.ReadByte(position++);
            value |= (b & 0x7F) << shift;
            if (b < 0x80) return value;
            if ((shift += 7) > 28) throw new InvalidDataException("词库索引已损坏。");
        }
    }
    private string Text(ref long position)
    {
        var bytes = new byte[Prefix(ref position)];
        index.ReadArray(position, bytes, 0, bytes.Length);
        position += bytes.Length;
        return Encoding.UTF8.GetString(bytes);
    }
    private string Word(int row) { long position = rows[row]; return Text(ref position); }
    // The fields after a row's word: the entry's offset and length in lexicon.dat, then its frequency.
    private long Fields(int row) { long position = rows[row]; var word = Prefix(ref position); return position + word; }
    private int Frequency(int row) => index.ReadInt32(Fields(row) + 12);

    private int LowerBound(string query)
    {
        int lo = 0, hi = rows.Length;
        while (lo < hi) { int mid = lo + (hi - lo) / 2; if (string.CompareOrdinal(Word(mid), query) < 0) lo = mid + 1; else hi = mid; }
        return lo;
    }
    private LocalEntry? Exact(string query)
    {
        var i = LowerBound(query);
        if (i >= rows.Length || Word(i) != query) return null;
        var fields = Fields(i);
        var bytes = new byte[index.ReadInt32(fields + 8)];
        var offset = index.ReadInt64(fields);
        lock (data) { data.Seek(offset, SeekOrigin.Begin); data.ReadExactly(bytes); }
        return JsonSerializer.Deserialize<LocalEntry>(bytes, Json.Options);
    }
    public LocalEntry? Lookup(string query)
    {
        query = Queries.Normalize(query);
        var hit = Exact(query);
        if (hit != null) return hit;
        return forms.TryGetValue(query, out var lemma) ? Exact(lemma) : null;
    }
    public LocalEntry? LookupInflectionLemma(string query) =>
        forms.TryGetValue(Queries.Normalize(query), out var lemma) ? Exact(lemma) : null;
    public string[] Suggest(string query, int limit = 7)
    {
        query = Queries.Normalize(query);
        if (query.Length == 0) return [];
        var candidates = new List<(string Word, int Frequency)>();
        for (var i = LowerBound(query); i < rows.Length && candidates.Count < 1200; i++)
        {
            var word = Word(i);
            if (!word.StartsWith(query, StringComparison.Ordinal)) break;
            candidates.Add((word, Frequency(i)));
        }
        return candidates.OrderBy(x => x.Word == query ? 0 : 1).ThenBy(x => x.Frequency > 0 ? x.Frequency : 1000000).ThenBy(x => x.Word.Length).Take(limit).Select(x => x.Word).ToArray();
    }
    public void Dispose() { data.Dispose(); index.Dispose(); map.Dispose(); }
}
