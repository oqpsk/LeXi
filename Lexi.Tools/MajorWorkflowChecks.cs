using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Lexi.Core;

internal static class MajorWorkflowChecks
{
    public static async Task Run(string fixture, string root)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
        var work = Path.Combine(Path.GetFullPath(root), "major-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
        var first = new LookupModel("codex", "synthetic-first"); var second = new LookupModel("claude", "synthetic-second");
        var settings = new AppSettings { Provider = first.Provider, Model = first.Model, SecondaryProvider = second.Provider, SecondaryModel = second.Model, MergeSenses = true };
        var cache = new Cache(Path.Combine(work, "retry")); int firstCalls = 0, secondCalls = 0, judgeCalls = 0;
        AiEntry Entry(string query) => new(query, true, [new("noun", "An isolated computing environment.", "独立的计算环境。", "计算机", "", "", [])], "", "unverified");
        var failMerge = false;
        var coordinator = new DictionaryCoordinator(cache, (model, query, evidence, options, token, preview) =>
        {
            if (model == first) firstCalls++;
            else if (++secondCalls == 1) throw new TimeoutException("synthetic failure");
            return Task.FromResult(model == first ? Entry(query) : Entry(query) with { Senses = [Entry(query).Senses[0] with { English = "A separate environment for isolated computation." }] });
        }, (request, options, token) =>
        {
            judgeCalls++; if (failMerge) throw new TimeoutException("synthetic judge failure");
            return Task.FromResult(new SenseMergePlan(request.InputHash, [], Overview: [], ComparedPairs: request.Pairs.Length));
        });
        var initial = await coordinator.LookupAsync("synthetic", null, settings, true, CancellationToken.None);
        Check(firstCalls == 1 && secondCalls == 1 && initial.Answers.Count(a => a.Phase == ModelPhase.Failed) == 1, "retry fixture retains successful answer beside a failed model");
        var retried = await coordinator.LookupAsync("synthetic", null, settings, true, CancellationToken.None, resume: initial);
        Check(firstCalls == 1 && secondCalls == 2 && retried.Answers.All(a => a.Phase == ModelPhase.Complete), "retry invokes only the failed model and preserves successful provider output");
        failMerge = true;
        var mergeFailed = await coordinator.LookupAsync("synthetic", null, settings, true, CancellationToken.None, resume: retried);
        var before = (firstCalls, secondCalls, judgeCalls); failMerge = false;
        var mergeRetried = await coordinator.LookupAsync("synthetic", null, settings, true, CancellationToken.None, resume: mergeFailed);
        Check(mergeFailed.MergeFailed && !mergeRetried.MergeFailed && firstCalls == before.firstCalls && secondCalls == before.secondCalls && judgeCalls == before.judgeCalls + 1,
            "merge-only retry never regenerates successful definitions");

        var sourceReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var modelVisible = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var modelStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new SlowSource(sourceReleased.Task);
        var queryRoot = Path.Combine(work, "session");
        var providers = new QueryProviders((model, query, evidence, options, token, progress) =>
        { modelStarted.TrySetResult(); return Task.FromResult(Entry(query)); }, (_,_,_) => throw new Exception("Judge disabled"), (_,_,_,_,_) => throw new Exception("No translation"));
        using (var session = new QuerySession(new("assets", Path.GetFullPath(fixture), queryRoot), providers: providers, network: handler))
        {
            session.Settings.Provider = "codex"; session.Settings.Model = "synthetic"; session.Settings.ParallelLookup = false; session.Settings.MergeSenses = false;
            session.Changed += view => { if (view.Answers?.Senses.Length > 0) modelVisible.TrySetResult(); };
            var run = session.SubmitAsync("quickfixture");
            await Task.WhenAll(modelStarted.Task, modelVisible.Task).WaitAsync(TimeSpan.FromSeconds(4));
            Check(!sourceReleased.Task.IsCompleted && !run.IsCompleted && session.View.Answers?.Senses.Length > 0, "first model output appears while slow online source is still pending");
            sourceReleased.TrySetResult(); await run;
            Check(session.View.Progress?.Busy == false && session.View.Answers?.Senses[0].Citations.Length == 0, "late source never retroactively creates model citations and completion waits for terminal stages");
            var networkCalls = handler.Calls; await session.SubmitAsync("quickfixture");
            Check(handler.Calls == networkCalls, "completed saved query performs no new network request");
            var lease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var management = session.ManageDataAsync(async () => { await lease.Task; return true; });
            await session.SubmitAsync("blockedquery");
            Check(session.Current == "quickfixture", "data maintenance cannot race a newly submitted query"); lease.TrySetResult(); await management;
        }

        var retryRoot = Path.Combine(work, "session-retry");
        int completedCalls = 0, failedCalls = 0;
        var retryProviders = new QueryProviders((model, query, evidence, options, token, report) =>
        {
            if (model == first) completedCalls++;
            else if (++failedCalls == 1) throw new TimeoutException("synthetic model failure");
            return Task.FromResult(Entry(query));
        }, (_,_,_) => throw new Exception("No judge"), (_,_,_,_,_) => throw new Exception("No translation"));
        using (var retrySession = new QuerySession(new("assets", Path.GetFullPath(fixture), retryRoot), providers: retryProviders, network: new SlowSource(Task.CompletedTask)))
        {
            retrySession.Settings.Provider = first.Provider; retrySession.Settings.Model = first.Model;
            retrySession.Settings.SecondaryProvider = second.Provider; retrySession.Settings.SecondaryModel = second.Model;
            retrySession.Settings.MergeSenses = false; retrySession.SaveSettings();
            await retrySession.SubmitAsync("retryfixture");
            Check(retrySession.View.CanRetry && completedCalls == 1 && failedCalls == 1, "session exposes retry only after an incomplete terminal outcome");
        }
        using (var reopened = new QuerySession(new("assets", Path.GetFullPath(fixture), retryRoot), providers: retryProviders, network: new SlowSource(Task.CompletedTask)))
        {
            await reopened.SubmitAsync("retryfixture");
            Check(reopened.View.CanRetry && completedCalls == 1 && failedCalls == 1, "a saved incomplete result restores the exact retry checkpoint without new calls");
            reopened.Settings.SpeechVoiceName = "unrelated speech preference";
            await reopened.RetryFailedAsync();
            Check(!reopened.View.CanRetry && completedCalls == 1 && failedCalls == 2 && reopened.View.Progress?.Outcome == QueryOutcome.Complete,
                "session retry after restart preserves successful work and ignores unrelated speech settings");
        }

        using (var failedNetwork = new FailingSource())
        using (var sourceSession = new QuerySession(new("assets", Path.GetFullPath(fixture), Path.Combine(work,"source-retry")), providers: providers, network: failedNetwork))
        {
            sourceSession.Settings.ParallelLookup = false; sourceSession.Settings.MergeSenses = false;
            await sourceSession.SubmitAsync("sourcefixture");
            Check(sourceSession.View.CanRetry && sourceSession.Saved.Dictionary("sourcefixture")?.FailedSources == SourceFailures.Online, "failed source is recorded separately from successful model generation");
            var calls = failedNetwork.Calls; failedNetwork.Fail = false;
            await sourceSession.RetryFailedAsync();
            Check(!sourceSession.View.CanRetry && failedNetwork.Calls == calls + 1, "source-only retry performs exactly the failed dictionary request");
            sourceSession.ShowHome(); Check(!sourceSession.View.CanRetry, "home never retains an actionable retry from a previous query");
        }

        int mergeDefinitions = 0, mergeAttempts = 0;
        var retryMerging = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mergeProviders = new QueryProviders((model, query, evidence, options, token, report) =>
        { mergeDefinitions++; return Task.FromResult(Entry(query) with { Senses = [Entry(query).Senses[0] with { English = "Environment variant " + model.Provider }] }); },
            async (request, options, token) =>
            {
                mergeAttempts++;
                if (mergeAttempts == 1) throw new InvalidDataException("synthetic judge response");
                if (mergeAttempts == 2) { retryMerging.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
                return new(request.InputHash, [], Overview: [], ComparedPairs: request.Pairs.Length);
            }, (_,_,_,_,_) => throw new Exception("No translation"));
        using (var mergeSession = new QuerySession(new("assets", Path.GetFullPath(fixture), Path.Combine(work,"cancelled-merge")), providers: mergeProviders, network: new SlowSource(Task.CompletedTask)))
        {
            await mergeSession.SubmitAsync("mergefixture"); Check(mergeSession.View.CanRetry, "failed merge can be retried from its completed model checkpoint");
            var run = mergeSession.RetryFailedAsync(); await retryMerging.Task.WaitAsync(TimeSpan.FromSeconds(3)); mergeSession.Cancel(); await run;
            Check(mergeSession.View.CanRetry && mergeDefinitions == 2, "cancelled merge retry remains retryable without discarding successful definitions");
            await mergeSession.RetryFailedAsync();
            Check(!mergeSession.View.CanRetry && mergeDefinitions == 2 && mergeAttempts == 3, "third merge attempt reuses both successful model answers after cancellation");
        }

        var historyRoot = Path.Combine(work, "data"); var history = new QueryHistory(historyRoot);
        for (int i = 0; i < 75; i++) history.Record("history-" + i, false);
        history.Record("ABS", false); history.Record("ABS", false); history.Record("abs", false); history.Record("A long sentence.", true);
        Check(new QueryHistory(historyRoot).Entries.Count > 30 && history.Search("history-", 50).Length == 25 && history.Suggest("ABS")[0] == "ABS" && !history.Suggest("A").Contains("A long sentence."),
            "history persists beyond 30, paginates and ranks exact case and repeated dictionary submissions");
        var dataCache = new Cache(historyRoot);
        await dataCache.WriteAsync("ai-v6", "sample", new { value = "intermediate" });
        await dataCache.WriteAsync(SavedQueries.TranslationKind, "sample", new SavedTranslation(new("fixture", "zh", ["测试。"], ""), first, DateTimeOffset.UtcNow));
        Directory.CreateDirectory(Path.Combine(historyRoot,"speech-cache")); File.WriteAllText(Path.Combine(historyRoot,"speech-cache","sample.mp3"),"synthetic audio");
        Directory.CreateDirectory(Path.Combine(historyRoot,"text-provider-keys")); File.WriteAllText(Path.Combine(historyRoot,"text-provider-keys","synthetic.bin"),"must stay private");
        settings.CodexPath = "machine-specific-client";
        var data = new UserData(historyRoot); var archive = Path.Combine(work,"backup.lexibackup"); await data.ExportAsync(archive, settings, history);
        using (var zip = ZipFile.OpenRead(archive))
        {
            Check(zip.Entries.All(e => !e.FullName.Contains("keys") && !e.FullName.Contains("speech") && !e.FullName.StartsWith("ai-v6")), "migration backup excludes keys, audio and intermediate cache by allowlist");
            using var json = JsonDocument.Parse(zip.GetEntry("settings.json")!.Open());
            Check(json.RootElement.GetProperty("codexPath").GetString() == "", "backup removes machine-specific client path");
        }
        var targetRoot = Path.Combine(work,"restored"); var targetData = new UserData(targetRoot); var targetHistory = new QueryHistory(targetRoot); var targetSettings = new AppSettings { Model = "keep" };
        var preview = targetData.Inspect(archive); targetData.Import(archive,targetSettings,targetHistory,false);
        Check(preview.History > 30 && preview.Results == 1 && targetHistory.Entries.Count == history.Entries.Count && targetSettings.Model == "keep", "preview counts and selective migration preserve current settings");
        data.Clear("intermediate");
        Check(data.Usage().Single(c=>c.Id=="intermediate").Files == 0 && data.Usage().Single(c=>c.Id=="results").Files == 1 && history.Entries.Count > 30 && File.Exists(Path.Combine(historyRoot,"text-provider-keys","synthetic.bin")),
            "clearing intermediate cache leaves completed queries, history and credentials intact");
        var beforeHistory = File.ReadAllBytes(Path.Combine(targetRoot,"history.json"));
        foreach (var badName in new[] { "../escape.json", "text-provider-keys/stolen.bin", "history.json/history.json" })
        {
            var bad = Path.Combine(work, Guid.NewGuid().ToString("N") + ".zip");
            using (var zip = ZipFile.Open(bad, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry(badName).Open()); writer.Write("{}"); }
            var rejected = false; try { targetData.Import(bad,targetSettings,targetHistory,true); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && File.ReadAllBytes(Path.Combine(targetRoot,"history.json")).SequenceEqual(beforeHistory), "unsafe archive paths are rejected before any data mutation");
        }
        using var metadata = new SpeechClient(new ModelsHandler());
        var models = await SpeechModels.RefreshAsync(work,metadata,true,"synthetic-key",CancellationToken.None);
        Check(models is [{ Id: "future_tts" }] && SpeechModels.Load(work,new()).Any(m=>m.Id=="future_tts"), "speech model discovery filters non-TTS models and persists a reusable catalog");
        Check(new AppSettings().CompatibleBaseUrl.Length == 0 && CompatibleText.DefaultBaseUrl.Length == 0, "fresh installations contain no personal API endpoint");
    }
    private sealed class SlowSource(Task release) : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            if (request.RequestUri!.Host.Contains("wiktionary")) await release.WaitAsync(token);
            return new(HttpStatusCode.NotFound) { Content = new StringContent("") };
        }
    }
    private sealed class FailingSource : HttpMessageHandler
    {
        public bool Fail = true;
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if (request.RequestUri!.Host.Contains("wiktionary")) { Calls++; if (Fail) throw new HttpRequestException("synthetic source failure"); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") });
        }
    }
    private sealed class ModelsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath != "/v1/models" || !request.Headers.Contains("xi-api-key")) throw new Exception("Wrong model discovery request");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[{\"model_id\":\"future_tts\",\"name\":\"Future voice model\",\"can_do_text_to_speech\":true},{\"model_id\":\"conversion\",\"name\":\"Conversion\",\"can_do_text_to_speech\":false}]") });
        }
    }
}
