namespace Lexi.Core;

// One instance per accepted query; monotonic time includes routing, sources,
// cached results, generation, sense judgment and pronunciation completion.
public sealed class LookupTiming
{
    private readonly TimeProvider clock;
    private readonly long started;
    public TimeSpan? FirstResult { get; private set; }
    public TimeSpan? Total { get; private set; }
    public LookupTiming(TimeProvider? clock = null)
    { this.clock = clock ?? TimeProvider.System; started = this.clock.GetTimestamp(); }
    public void MarkFirst()
    { if (Total == null) FirstResult ??= clock.GetElapsedTime(started); }
    public string Finish(string status, string firstLabel = "首条释义")
    {
        Total ??= clock.GetElapsedTime(started);
        var first = FirstResult is { } time ? FormattableString.Invariant($"{firstLabel} {time.TotalSeconds:0.0} 秒 · ") : "";
        return first + FormattableString.Invariant($"总耗时 {Total.Value.TotalSeconds:0.0} 秒 · ") + status;
    }
}
