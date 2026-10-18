using System.Text.Json;

namespace Lexi.Core;

// Stage times in milliseconds from submitting a query: first (any result), local, source, sense
// (first bilingual sense), tldr, stream:<model> (first streamed sense), model:<model>, merge,
// context and total.
public sealed record TimingRecord(string Date, string Kind, string Path, string[] Models, bool Context, Dictionary<string, int> Ms);
public sealed record TimingSummary(string Path, int Count, Dictionary<string, (int P50, int P90)> Stages);

// How long lookups take on this machine, so time to a useful result can be tracked across
// changes. It holds the day, the route and stage times only: never the query, its sentence or
// an answer. It stays local, is trimmed to the latest records and clears with intermediate caches.
public sealed class TimingLog(string root)
{
    public const string Folder = "diagnostics";
    public const string FileName = "lookup-timings-v1.jsonl";
    public const int Keep = 1000;
    private readonly object sync = new();
    private string FilePath => Path.Combine(root, Folder, FileName);

    public void Append(TimingRecord record)
    {
        try
        {
            lock (sync)
            {
                Directory.CreateDirectory(Path.Combine(root, Folder));
                File.AppendAllText(FilePath, JsonSerializer.Serialize(record, Json.Options) + "\n");
                var lines = File.ReadAllLines(FilePath);
                if (lines.Length <= Keep + Keep / 5) return;
                var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllLines(temp, lines[^Keep..]);
                File.Move(temp, FilePath, true);
            }
        }
        // Measuring never gets in the way of a lookup.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public TimingRecord[] Read()
    {
        lock (sync)
        {
            if (!File.Exists(FilePath)) return [];
            return File.ReadLines(FilePath).Select(line =>
            {
                try { return JsonSerializer.Deserialize<TimingRecord>(line, Json.Options); }
                catch (JsonException) { return null; }
            }).OfType<TimingRecord>().ToArray();
        }
    }

    public static TimingSummary[] Summarize(IEnumerable<TimingRecord> records) => records.GroupBy(r => r.Kind + " · " + r.Path)
        .OrderByDescending(g => g.Count()).Select(g => new TimingSummary(g.Key, g.Count(), g.SelectMany(r => r.Ms)
            .GroupBy(p => p.Key).OrderBy(p => p.Min(v => v.Value)).ToDictionary(p => p.Key, p =>
            {
                var values = p.Select(v => v.Value).Order().ToArray();
                int At(double q) => values[Math.Min(values.Length - 1, (int)Math.Ceiling(q * values.Length) - 1)];
                return (At(0.5), At(0.9));
            }))).ToArray();
}
