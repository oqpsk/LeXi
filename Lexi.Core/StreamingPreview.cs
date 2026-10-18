using System.Text.Json;

namespace Lexi.Core;

// Token bursts must not turn into an unbounded queue of JSON parsing and UI work.
// Final responses bypass this preview sampler and still undergo full validation.
public sealed class StreamingPreview<T>(Func<string, T?> parse, IProgress<T>? progress,
    CancellationToken token, TimeProvider? clock = null) where T : class
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private long? sampled;
    private string? previous;
    public void Report(string text)
    {
        token.ThrowIfCancellationRequested();
        if (progress == null) return;
        var now = time.GetTimestamp();
        if (sampled is { } last && time.GetElapsedTime(last, now) < TimeSpan.FromMilliseconds(80)) return;
        sampled = now;
        if (parse(text) is not { } entry) return;
        var fingerprint = JsonSerializer.Serialize(entry, Json.Options);
        if (fingerprint == previous) return;
        previous = fingerprint;
        token.ThrowIfCancellationRequested();
        progress.Report(entry);
    }
}
