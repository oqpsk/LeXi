using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lexi.Core;

public sealed record HistoryEntry(string Query, bool Translation, DateTimeOffset QueriedAt, int Visits = 1)
{
    [JsonIgnore]
    public string Preview => string.Join(" ", Query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    [JsonIgnore]
    public string Kind => Translation ? "翻译" : "词条";
}

// Local user data, independent of definition caches. Only explicit submissions
// enter history; deleting a row never deletes or reimports cached definitions.
public sealed class QueryHistory
{
    public const int Capacity = 10000;
    public const int HomePageSize = 50;
    private readonly string path;
    private HistoryEntry[] entries = [];
    public IReadOnlyList<HistoryEntry> Entries => Array.AsReadOnly(entries);
    public string Error { get; private set; } = "";
    public QueryHistory(string root)
    {
        path = Path.Combine(root, "history.json");
        try
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > 32_000_000) throw new InvalidDataException("History size limit");
            entries = (JsonSerializer.Deserialize<HistoryEntry[]>(File.ReadAllText(path), Json.Options) ?? [])
                .Where(e => e?.Query != null && Queries.Validate(e.Query) == null)
                .Select(e => e with { Query = Canonical(e.Query, e.Translation) })
                .OrderByDescending(e => e.QueriedAt).DistinctBy(e => (e.Query, e.Translation)).Take(Capacity).ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { Error = "历史记录暂时无法读取。"; }
    }
    private static string Canonical(string query, bool translation) => translation ? query.Trim().Replace("\r\n", "\n").Replace('\r','\n') : Queries.LookupKey(query);
    public bool Record(string query, bool translation, DateTimeOffset? when = null)
    {
        if (Queries.Validate(query) != null) return false;
        var key = Canonical(query, translation);
        var previous = entries.FirstOrDefault(e => e.Query == key && e.Translation == translation);
        var instant = when ?? DateTimeOffset.UtcNow;
        var entry = new HistoryEntry(key, translation, previous != null && previous.QueriedAt > instant ? previous.QueriedAt : instant, Math.Min(1_000_000, Math.Max(0, previous?.Visits ?? 0) + 1));
        return Save(new[] { entry }.Concat(entries.Where(e => e.Query != entry.Query || e.Translation != translation)).OrderByDescending(e => e.QueriedAt).Take(Capacity).ToArray());
    }
    public HistoryEntry[] Search(string text, int offset = 0, int count = HomePageSize) => entries
        .Where(e => e.Query.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase))
        .Skip(Math.Max(0, offset)).Take(Math.Clamp(count, 1, 200)).ToArray();
    public string[] Suggest(string text)
    {
        var prefix = Queries.LookupKey(text);
        if (prefix.Length == 0 || prefix.Length > 100) return [];
        return entries.Where(e => !e.Translation && e.Query.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Query.Equals(prefix, StringComparison.Ordinal))
            .ThenByDescending(e => e.Query.StartsWith(prefix, StringComparison.Ordinal))
            .ThenByDescending(e => e.Visits).ThenByDescending(e => e.QueriedAt).Take(8).Select(e => e.Query).ToArray();
    }
    public bool Merge(IEnumerable<HistoryEntry> imported) => Save(entries.Concat(imported)
        .GroupBy(e => (e.Query, e.Translation)).Select(g => g.OrderByDescending(e => e.QueriedAt).First() with { Visits = g.Max(e => e.Visits) })
        .OrderByDescending(e => e.QueriedAt).Take(Capacity).ToArray());
    public bool Remove(HistoryEntry entry) => Save(entries.Where(e => e.Query != entry.Query || e.Translation != entry.Translation).ToArray());
    private bool Save(HistoryEntry[] next)
    {
        // 10,000 short queries fit readily; long translation histories also obey the file budget.
        long budget = 0; next = next.TakeWhile(e => (budget += (long)e.Query.Length * 6 + 256) <= 30_000_000).ToArray();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(next, Json.Options));
            File.Move(temporary, path, true);
            entries = next; Error = ""; return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { Error = "历史记录未能保存，请检查文件夹权限。"; return false; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
