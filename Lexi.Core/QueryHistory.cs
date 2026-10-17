using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lexi.Core;

public sealed record HistoryEntry(string Query, bool Translation, DateTimeOffset QueriedAt)
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
    public const int Capacity = 30;
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
            if (new FileInfo(path).Length > 2_000_000) throw new InvalidDataException("History size limit");
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
        var entry = new HistoryEntry(Canonical(query, translation), translation, when ?? DateTimeOffset.UtcNow);
        return Save(new[] { entry }.Concat(entries.Where(e => e.Query != entry.Query || e.Translation != translation)).Take(Capacity).ToArray());
    }
    public bool Remove(HistoryEntry entry) => Save(entries.Where(e => e.Query != entry.Query || e.Translation != entry.Translation).ToArray());
    private bool Save(HistoryEntry[] next)
    {
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
