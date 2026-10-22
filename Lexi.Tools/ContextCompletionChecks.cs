using System.Net;
using Lexi.Core;

internal static class ContextCompletionChecks
{
    internal static async Task Run(string fixture, string root)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
        var folder = Path.Combine(root, "context-completion-" + Guid.NewGuid().ToString("N"));
        var clock = new ManualClock();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entry = new AiEntry("bank", true, [new("noun", "a financial institution", "银行", "", "", "", [], Gloss: "银行", CoreMeaning: true),
            new("noun", "the side of a river", "河岸", "", "", "", [], Gloss: "河岸")], "", "unverified");
        var calls = 0;
        var failPick = false;
        var pickCalls = 0;
        var providers = new QueryProviders((_, _, _, _, _, _) => { calls++; return Task.FromResult(entry); },
            (_, _, _) => throw new Exception("Merging is disabled"), (_, _, _, _, _) => throw new Exception("No translation"),
            async (asked, _, token) =>
            {
                pickCalls++;
                started.TrySetResult();
                if (failPick) throw new IOException("Synthetic context failure");
                await release.Task.WaitAsync(token);
                return new(asked.InputHash, asked.Senses.Single(s => s.Chinese == "河岸").Id, "河岸");
            });
        using var network = new NotFound();
        using var session = new QuerySession(new(folder, Path.GetFullPath(fixture), Path.Combine(folder, "user")), providers: providers, network: network, clock: clock);
        session.Settings.Model = "synthetic-context-completion"; session.Settings.Provider = "codex";
        session.Settings.ParallelLookup = false; session.Settings.MergeSenses = false;
        await session.InitializeAsync();
        var views = new List<QueryView>();
        session.Changed += view => { lock (views) views.Add(view); };

        var live = session.SubmitAsync("bank", context: "We sat on the bank of the river.");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(30);
        Check(!live.IsCompleted && session.View.Progress is { Busy: true } && !session.View.Status.Contains("总耗时") &&
            session.View.Progress.Items.Any(i => i.Key == "context" && i.State == QueryWorkState.Querying) &&
            session.View.Progress.Items.Where(i => i.Key != "context").All(i => i.State == QueryWorkState.Complete),
            "a slower sentence pick keeps the live query busy after all definitions and merging end");
        lock (views) Check(views.All(v => v.Progress is not { Busy: false }), "no premature terminal view can trigger success retirement");
        clock.Advance(5000); release.TrySetResult(); await live;
        var log = new TimingLog(Path.Combine(folder, "user"));
        Check(session.View is { QuickRead.Text: "河岸", Progress.Busy: false } && log.Read()[0].Ms["total"] >= 5000 &&
            log.Read()[0].Ms["total"] >= log.Read()[0].Ms["context"], "total elapsed time includes a slower sentence pick");

        started = new(TaskCreationOptions.RunContinuationsAsynchronously); release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var cached = session.SubmitAsync("bank", context: "The boat reached the bank.");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Check(calls == 1 && session.View.Progress is { Busy: true, Restored: true } && !session.View.Status.Contains("总耗时"),
            "a saved entry restores without redefining it and waits for its new sentence before finishing");
        clock.Advance(3000); release.TrySetResult(); await cached;
        Check(log.Read()[1].Ms["total"] >= 3000 && session.View.Progress is { Busy: false, Restored: true },
            "saved-entry totals also include sentence judgment and keep restoration semantics");

        started = new(TaskCreationOptions.RunContinuationsAsynchronously); release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = session.SubmitAsync("bank", context: "We walked along the bank at sunset.");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        session.Cancel(); await cancelled;
        await session.SubmitAsync("bank");
        release.TrySetResult(); await Task.Delay(30);
        Check(session.View.Progress is { Busy: false } && !session.View.QuickRead.Caption.Contains("本句") &&
            session.View.Answers!.Senses.All(s => !s.Heading.InContext), "cancelling a sentence pick cannot overlay a subsequent ordinary lookup");
        failPick = true;
        await session.SubmitAsync("bank", context: "The ducks rested beside the bank.");
        Check(session.View.Progress is { Busy: false, Warning: true } && session.View.CanRetry &&
            session.View.Progress.Items.Any(i => i.Key == "context" && i.State == QueryWorkState.Failed),
            "a failed sentence pick stays explicit and never produces a success retirement");
        var beforeRetry = pickCalls;
        failPick = false;
        await session.RetryFailedAsync();
        Check(calls == 1 && pickCalls == beforeRetry + 1 && session.View is { QuickRead.Text: "河岸", Progress.Warning: false, CanRetry: false },
            "retrying sentence judgment preserves successful definitions and performs only the failed step");
        session.Dispose(); Directory.Delete(folder, true);
    }
    private sealed class ManualClock : TimeProvider
    {
        private long now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Interlocked.Read(ref now);
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddMilliseconds(GetTimestamp());
        internal void Advance(long ms) => Interlocked.Add(ref now, ms);
    }
    private sealed class NotFound : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
