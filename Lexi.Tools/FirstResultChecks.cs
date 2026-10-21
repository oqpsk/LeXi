using System.Net;
using Lexi.Core;

// Time to a useful result: a finished streamed core sense previews the TL;DR, the sentence's
// meaning is chosen while senses are still being merged, and each lookup's stage times are logged.
internal static class FirstResultChecks
{
    public static async Task Run(string fixture, string root)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }

        var work = Path.Combine(Path.GetFullPath(root), "first-result-" + Guid.NewGuid().ToString("N"));
        var primary = new LookupModel("codex", "synthetic-first");
        var secondary = new LookupModel("claude", "synthetic-second");
        AiSense Sense(string english, string chinese, bool? core = null) =>
            new("noun", english, chinese, "", "", "", [], Gloss: chinese, CoreMeaning: core, ChineseTerm: chinese);
        var river = Sense("the land beside a river", "河岸", true);
        var first = new AiEntry("bank", true, [Sense("a financial institution", "银行", false), river], "", "unverified");
        var second = new AiEntry("bank", true, [Sense("the sloping side of a river", "河边", true)], "", "unverified");

        var streamed = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var picked = new TaskCompletionSource();
        bool judgedAfterPick = false;
        int picks = 0;
        var wanted = "河岸";
        var providers = new QueryProviders(async (model, _, _, _, token, preview) =>
            {
                if (model != primary) return second;
                // The model has finished writing its core sense; the rest is still on its way.
                preview.Report(first with { Senses = [river] });
                streamed.TrySetResult();
                await release.Task.WaitAsync(token);
                return first;
            },
            async (request, _, _) =>
            {
                // The merge waits for the sentence's meaning; it only arrives if the pick did not wait for the merge.
                judgedAfterPick = await Task.WhenAny(picked.Task, Task.Delay(5000)) == picked.Task;
                return new SenseMergePlan(request.InputHash, request.Pairs.Select(p =>
                {
                    var chinese = new[] { p.A, p.B }.Select(id => request.Candidates.Single(c => c.Id == id).Sense.Chinese).ToArray();
                    return new SenseVerdict(p.A, p.B, chinese.All(c => c.StartsWith('河')) ? "equivalent" : "different", 1);
                }).ToArray());
            },
            (_, _, _, _, _) => throw new Exception("No translation in this check"),
            (asked, _, _) =>
            {
                Interlocked.Increment(ref picks);
                var sense = asked.Senses.Single(s => s.Chinese == wanted);
                picked.TrySetResult();
                return Task.FromResult(new ContextPick(asked.InputHash, sense.Id, "河岸（这里指河边的地）"));
            });
        using var network = new NotFound();
        using var session = new QuerySession(new(work, Path.GetFullPath(fixture), Path.Combine(work, "user")), providers: providers, network: network);
        session.Settings.Provider = primary.Provider; session.Settings.Model = primary.Model;
        session.Settings.ParallelLookup = true; session.Settings.SecondaryProvider = secondary.Provider; session.Settings.SecondaryModel = secondary.Model;
        session.Settings.MergeSenses = true; session.Settings.JudgeProvider = "codex"; session.Settings.JudgeModel = "synthetic-judge";
        await session.InitializeAsync();

        var views = new List<QueryView>();
        session.Changed += view => { lock (views) views.Add(view); };
        const string sentence = "We sat on the bank of the river.";
        var lookup = session.SubmitAsync("bank", context: sentence);
        await streamed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(50);
        QueryView[] seen; lock (views) seen = [.. views];
        Check(seen.Any(v => v.QuickRead is { Visible: true, Text: "河岸", State: "预览", ShowState: true }),
            "a finished streamed core sense leads the TL;DR as a preview before any model completes");
        release.SetResult();
        await lookup;
        var view = session.View;
        var marked = view.Answers!.Senses.Where(s => s.Heading.InContext).ToArray();
        Check(judgedAfterPick && picks == 1, "the sentence's meaning is chosen while the senses are still being merged");
        Check(view.QuickRead is { Text: "河岸（这里指河边的地）", Caption: "TL;DR · 本句语境" } && marked.Length == 1 &&
            marked[0].Chinese.StartsWith('河') && marked[0].Alternatives.Length == 1,
            "a sense picked before the merge marks the merged group it joined");

        await session.SubmitAsync("bank", context: sentence);
        Check(picks == 1 && session.View.QuickRead.Caption == "TL;DR · 本句语境",
            "the merged page restored from its cache finds the pick made during the live lookup");
        wanted = "河边";
        await session.SubmitAsync("bank", context: "The boat drifted towards the bank.");
        var other = session.View.Answers!.Senses.Where(s => s.Heading.InContext).ToArray();
        Check(picks == 2 && other.Length == 1 && other[0].Key == marked[0].Key,
            "either wording of a merged sense marks the sense shown for the group");

        var log = new TimingLog(Path.Combine(work, "user"));
        var records = log.Read();
        var live = records.FirstOrDefault();
        Check(records.Length == 3 && live is { Kind: "dictionary", Path: "models", Context: true } && live.Models.Length == 2 &&
            new[] { "local", "source", "sense", "tldr", "merge", "context", "first", "total" }.All(live.Ms.ContainsKey) &&
            live.Ms["tldr"] <= live.Ms["model:codex/synthetic-first"] && records.Skip(1).All(r => r.Path == "saved"),
            "each lookup logs when its TL;DR, senses, models, merge and sentence's meaning arrived");
        var text = File.ReadAllText(Path.Combine(work, "user", TimingLog.Folder, TimingLog.FileName));
        Check(!text.Contains("bank") && !text.Contains("river") && !text.Contains("河"), "the timing log never holds the query, its sentence or an answer");
        Check(TimingLog.Summarize(records) is [{ Path: "dictionary · saved", Count: 2 }, { Path: "dictionary · models", Count: 1 }],
            "the timing report groups lookups by how they were answered");
        Check(new UserData(Path.Combine(work, "user")).Usage().Single(u => u.Id == "intermediate").Files >= 2,
            "the timing log and saved sentence picks count as intermediate caches");
        new UserData(Path.Combine(work, "user")).Clear("intermediate");
        Check(log.Read().Length == 0, "clearing intermediate caches clears the timing log");
        session.Dispose();
        Directory.Delete(work, true);
    }

    private sealed class NotFound : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
