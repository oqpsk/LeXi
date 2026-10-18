using System.Collections.Concurrent;
using System.Net;
using Lexi.Core;

internal static class ResponsivenessChecks
{
    public static async Task Run(string fixture, string root, Action<bool, string> check)
    {
        var clock = new Clock(); int parsed = 0; var updates = new List<TranslationEntry>();
        using var cancellation = new CancellationTokenSource();
        var sampled = new StreamingPreview<TranslationEntry>(text =>
        {
            parsed++;
            return text == "incomplete" ? null : new("input", "zh", [text], "");
        }, new Collector<TranslationEntry>(updates.Add), cancellation.Token, clock);
        sampled.Report("first");
        for (var i = 0; i < 10000; i++) sampled.Report("burst" + i);
        check(parsed == 1 && updates.Count == 1 && updates[0].Paragraphs[0] == "first", "a buffered token burst parses and publishes a bounded number of previews");
        clock.Advance(); sampled.Report("first");
        check(parsed == 2 && updates.Count == 1, "unchanged definitions never enqueue another preview after the sampling interval");
        clock.Advance(); sampled.Report("incomplete");
        clock.Advance(); sampled.Report("second");
        check(updates.Count == 2 && updates[1].Paragraphs[0] == "second", "later valid content appears while incomplete bilingual content stays hidden");
        cancellation.Cancel();
        try { sampled.Report("late"); check(false, "cancelled preview"); }
        catch (OperationCanceledException) { check(updates.Count == 2, "cancellation stops preview parsing and delivery immediately"); }

        var path = Path.Combine(Path.GetFullPath(root), "responsive-" + Guid.NewGuid().ToString("N"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SynchronizationContext? providerContext = null;
        var providers = new QueryProviders(
            (_, _, _, _, _, _) => throw new Exception("Unexpected dictionary request"),
            (_, _, _) => throw new Exception("Unexpected judge request"),
            async (text, target, _, token, preview) =>
            {
                providerContext = SynchronizationContext.Current;
                preview.Report(new(Cache.Hash(text), target, ["生成期间的预览。"], ""));
                entered.TrySetResult();
                try { await finish.Task.WaitAsync(token); }
                catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
                return new(Cache.Hash(text), target, ["完整译文。"], "");
            });
        using var network = new NoNetwork();
        using var session = new QuerySession(new(path, Path.GetFullPath(fixture), path), providers: providers, network: network);
        await session.InitializeAsync();
        session.Settings.ParallelLookup = false;
        var ui = new QueueContext(); var previous = SynchronizationContext.Current;
        Task generating;
        try
        {
            SynchronizationContext.SetSynchronizationContext(ui);
            generating = session.SubmitAsync("This is synthetic text for translation.", force: true);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        check(providerContext == null && !generating.IsCompleted && session.View.Translation is { Complete: false },
            "generation runs off the UI context and publishes a live preview before the final token");
        session.Cancel();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (var i = 0; i < 500 && !generating.IsCompleted; i++) { ui.Drain(); await Task.Delay(2); }
        await generating.WaitAsync(TimeSpan.FromSeconds(5));
        check(session.View.Progress is { Busy: false } && session.Saved.TranslationResult("This is synthetic text for translation.", "zh") == null,
            "input can cancel an active generation without caching a provisional translation");
        finish.TrySetResult();
        await session.SubmitAsync("This is the next synthetic translation.", force: true);
        check(session.View.Translation is { Complete: true } && session.View.Status.Contains("翻译完成") && session.View.Query == "This is the next synthetic translation.",
            "the complete latest result still arrives after an interrupted query");
    }
    private sealed class Collector<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
    private sealed class Clock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public void Advance() => ticks += TimeSpan.TicksPerMillisecond * 80;
    }
    private sealed class QueueContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<Action> queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => queue.Enqueue(() => callback(state));
        public void Drain() { while (queue.TryDequeue(out var action)) action(); }
    }
    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
