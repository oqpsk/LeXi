namespace Lexi.Core;

// One instance per accepted query; monotonic time includes routing, sources,
// cached results, generation, sense judgment and pronunciation completion.
// Stages record when each part first reached the reader (the TL;DR, the first sense, each
// model, the merge, the sentence's meaning), so time to a useful result can be tracked.
public sealed class LookupTiming
{
    private readonly TimeProvider clock;
    private readonly long started;
    private readonly Dictionary<string, TimeSpan> stages = [];
    public TimeSpan? FirstResult { get; private set; }
    public TimeSpan? Total { get; private set; }
    // How the query was answered: saved, cached, offline, sources, models or translation.
    public string Path { get; set; } = "";
    public LookupTiming(TimeProvider? clock = null)
    { this.clock = clock ?? TimeProvider.System; started = this.clock.GetTimestamp(); }
    public void MarkFirst()
    { if (Total == null) FirstResult ??= clock.GetElapsedTime(started); }
    // A stage keeps the first time it was reached. The sentence's meaning may arrive after Finish.
    public void Mark(string stage) { lock (stages) stages.TryAdd(stage, clock.GetElapsedTime(started)); }
    public IReadOnlyDictionary<string, TimeSpan> Stages { get { lock (stages) return new Dictionary<string, TimeSpan>(stages); } }
    public string Finish(string status, string firstLabel = "首条释义")
    {
        Total ??= clock.GetElapsedTime(started);
        var first = FirstResult is { } time ? FormattableString.Invariant($"{firstLabel} {time.TotalSeconds:0.0} 秒 · ") : "";
        return first + FormattableString.Invariant($"总耗时 {Total.Value.TotalSeconds:0.0} 秒 · ") + status;
    }
    public TimingRecord Record(string kind, string[] models, bool context, DateTimeOffset at)
    {
        var times = Stages.ToDictionary(p => p.Key, p => (int)Math.Round(p.Value.TotalMilliseconds));
        if (FirstResult is { } first) times["first"] = (int)Math.Round(first.TotalMilliseconds);
        if (Total is { } total) times["total"] = (int)Math.Round(total.TotalMilliseconds);
        return new(at.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), kind, Path.Length > 0 ? Path : "other", models, context, times);
    }
}
