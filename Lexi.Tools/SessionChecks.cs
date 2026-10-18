using System.Net;
using Lexi.Core;

// The query lifecycle that every front end renders, exercised with synthetic providers,
// a fixture dictionary and a network handler that never leaves the process.
internal static class SessionChecks
{
    public static async Task Run(string fixture, string root)
    {
        await MajorWorkflowChecks.Run(fixture,root);
        await InputPreviewChecks.Run(fixture,root);
        CandidateChecks.Run();
        await ContextChecks.Run(fixture, root);
        await FirstResultChecks.Run(fixture, root);
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
        var work = Path.Combine(Path.GetFullPath(root), Guid.NewGuid().ToString("N"));
        LexiPaths Paths(string name) => new(Path.Combine(work, "assets"), Path.GetFullPath(fixture), Path.Combine(work, name));
        int defines = 0, judges = 0, translations = 0;
        var providers = new QueryProviders(
            async (model, query, evidence, options, token, preview) =>
            {
                Interlocked.Increment(ref defines);
                if (query == "hello" && model.Provider == "claude") await Task.Delay(Timeout.Infinite, token);
                var entry = new AiEntry(query, true, [new("noun", model.Name + " synthetic meaning", "合成释义：" + model.Name, "", "", "", [])], "", "unverified", WordForms: []);
                preview.Report(entry);
                await Task.Yield();
                return entry;
            },
            (request, options, token) =>
            {
                Interlocked.Increment(ref judges);
                return Task.FromResult(new SenseMergePlan(request.InputHash, request.Pairs.Select(p => new SenseVerdict(p.A, p.B, "equivalent", 0.99)).ToArray()));
            },
            async (text, target, options, token, preview) =>
            {
                Interlocked.Increment(ref translations);
                var entry = new TranslationEntry(Cache.Hash(text), target, ["船已就绪。", "保留 42。"], "");
                preview.Report(entry with { Paragraphs = ["船已就绪。"] });
                await Task.Yield();
                return entry;
            });
        using var network = new NoNetwork();

        using (var offline = new QuerySession(Paths("offline"), providers: providers, network: network))
        {
            var views = new List<QueryView>(); offline.Changed += views.Add;
            offline.Settings.Online = false;
            await offline.InitializeAsync();
            Check(offline.View is { Kind: QueryViewKind.Home, Status: "准备就绪", Progress: null }, "a loaded library reports readiness before any query");
            await offline.SubmitAsync("  ");
            Check(offline.View.Status == "输入单词、短语或待翻译文本。" && offline.History.Entries.Count == 0, "invalid input reports a reason without recording history");
            await offline.SubmitAsync("Hello");
            var view = offline.View;
            Check(view is { Kind: QueryViewKind.Dictionary, Query: "Hello", Heading: "Hello", Progress: { Busy: false, Outcome: QueryOutcome.Complete, ModelsEnabled: false } } &&
                view.Local is [{ Chinese: "你好", Headword: "" }] && view.Status.Contains("离线模式") && defines == 0,
                "offline lookup shows the original dictionary text, finishes and never calls a model");
            Check(views.Select(v => v.Revision).SequenceEqual(views.Select(v => v.Revision).Order()) &&
                views.SkipWhile(v => v.Query != "Hello").TakeWhile(v => v.Progress?.Busy != false).All(v => v.Progress?.Busy == true),
                "published views are ordered and stay busy until the terminal step");
            await offline.SubmitAsync("walked");
            Check(offline.View.WordForms is [{ Cells: [{ Spelling: "walked → walk" }, ..] cells }] &&
                cells.Any(c => c.Label == "过去式 / 过去分词" && c.Matches) && offline.View.Local is [{ Headword: "词条：walk" }],
                "inflected lookups resolve to the lemma with combined recorded forms and a visible headword note");
            Check(offline.History.Entries.Select(e => e.Query).SequenceEqual(["walked", "Hello"]) && offline.View.Navigation is { CanBack: true, BackLabel: "Hello" },
                "explicit submissions enter history and the back target names the previous query");
            await offline.SubmitAsync("Hello");
            Check(offline.View.Status.Contains("已显示缓存") && offline.View.Progress?.Detail == "已恢复保存的结果 · 点击重新生成可更新",
                "a completed view is restored without network or provider work");
            var page = await offline.NavigateAsync(-1);
            Check(page?.Query == "walked" && offline.View.Query == "walked" && offline.View.Navigation is { CanForward: true, ForwardLabel: "Hello" } &&
                offline.History.Entries[0].Query == "Hello", "back navigation reopens the previous page without rewriting history order");
            offline.ShowHome();
            Check(offline.View is { Kind: QueryViewKind.Home, Progress: null, Status: "准备就绪", QuickRead.Visible: false } && offline.Current == "",
                "returning home clears the query lifecycle and lexical overview");
        }

        using (var online = new QuerySession(Paths("online"), providers: providers, network: network))
        {
            var views = new List<QueryView>(); online.Changed += views.Add;
            await online.InitializeAsync();
            await online.SubmitAsync("serendipity");
            var view = online.View;
            Check(defines == 2 && judges == 1 && view.Progress is { Busy: false, Outcome: QueryOutcome.Complete, Title: "已完成 · 1 个义项" } &&
                view.Answers?.Senses is [{ Alternatives.Length: 1, AlternativesLabel: "其他表述 · 1" }],
                "two completed models merge into one sense while the other wording stays accessible");
            Check(views.Any(v => v.Progress is { Stage: QueryStage.Supplement, Busy: true } p && p.Detail.Contains("预览")) &&
                views.Any(v => v.Progress is { Stage: QueryStage.Merge, Busy: true }) &&
                views.Where(v => v.Progress?.Busy == false).All(v => ReferenceEquals(v, view) || v.Revision > view.Revision),
                "model completion stays a preview through merging; only the final step settles the query");
            Check(view.Status.StartsWith("首条释义") && view.Status.Contains("查询完成 · 同义项已整理") && view.Status.Contains("在线词典未收录") &&
                view.QuickRead is { Visible: true, State: "已整理", Caption: "TL;DR · 已整理" } && view.Online == null,
                "the final status keeps timing, merge outcome and missing-source notes beside a settled overview");
            Check(view.Answers!.Senses[0] is { Provenance: var provenance, SourceNote.Length: > 0, Citations.Length: 0 } && provenance.Contains("不表示释义错误"),
                "unsourced model senses explain their provenance without implying an incorrect definition");
            await online.SubmitAsync("serendipity");
            Check(defines == 2 && online.View.Status.Contains("已显示缓存"), "saved model results are restored without another provider call");
            await online.RegenerateAsync();
            Check(defines == 4 && online.View.Progress?.Outcome == QueryOutcome.Complete, "explicit regeneration bypasses saved results");

            views.Clear();
            await online.SubmitAsync("The ship is ready.\nKeep 42 intact.");
            view = online.View;
            Check(translations == 1 && view is { Kind: QueryViewKind.Translation, Heading: "翻译", Pronunciation.Text: "英文 → 中文",
                Translation: { Complete: true, Paragraphs.Length: 2, Source: "Codex" }, Progress: { Busy: false, Translation: true } } &&
                view.Status.Contains("翻译完成") && views.Any(v => v.Translation is { Complete: false, Source: "Codex · 生成中" }),
                "multiline text routes to translation, previews stay provisional and the final paragraphs are kept");
            await online.SubmitAsync("The ship is ready.\nKeep 42 intact.");
            Check(translations == 1 && online.View.Status.Contains("已显示缓存翻译"), "a completed translation is restored with its producing model");

            views.Clear();
            var first = online.SubmitAsync("hello");
            await Until(() => online.View is { Query: "hello", Progress.Stage: QueryStage.Supplement });
            await online.SubmitAsync("go");
            await first;
            var takeover = views.First(v => v.Query == "go");
            Check(online.View.Query == "go" && views.SkipWhile(v => v != takeover).All(v => v.Request == takeover.Request),
                "a new query cancels the previous one and its late updates cannot reach the new page");
            var stalled = online.SubmitAsync("hello", true);
            await Until(() => online.View is { Query: "hello", Progress.Stage: QueryStage.Supplement });
            online.SetOnline(false);
            await stalled;
            Check(online.View.Progress is { Busy: false, Title: "查询已停止" } && online.View.Status.StartsWith("查询模式已更改 · 已保留当前结果"),
                "changing query settings stops the running lookup and keeps the current result");
        }

        var tracker = new QueryProgressTracker();
        var primary = new LookupModel("codex", "fixture"); var secondary = new LookupModel("claude", "fixture");
        AiEntry Entry(params string[] meanings) => new("fixture", true, meanings.Select(m => new AiSense("noun", m, m, "", "", "", [])).ToArray(), "", "unverified");
        var both = new[] { new ModelAnswer(primary, ModelPhase.Complete, Entry("a", "b")), new ModelAnswer(secondary, ModelPhase.Complete, Entry("c")) };
        var snapshot = new LookupSnapshot(both, SenseMerging.Group(both));
        var waiting = new LookupSnapshot([new(primary,ModelPhase.Pending,Entry("cached"),true)], []);
        var workSettings = new AppSettings { Provider=primary.Provider, Model=primary.Model, SecondaryProvider=secondary.Provider, SecondaryModel=secondary.Model, ParallelLookup=true, JudgeProvider="codex", JudgeModel="judge-fixture" };
        var pendingWork = QueryProgressState.Dictionary(true,true).WithWork(workSettings);
        Check(pendingWork.Items.Length == 3 && pendingWork.Items.All(i => i.State == QueryWorkState.Pending),
            "model slots wait for dictionary evidence without pretending generation has started");
        var workSnapshot = new LookupSnapshot([new(primary,ModelPhase.Streaming,Entry("new")),new(secondary,ModelPhase.Pending)],[]);
        var actorProgress = QueryProgressState.Updating(workSnapshot,true).WithWork(workSettings,workSnapshot);
        Check(actorProgress.Items.Select(i=>i.State).SequenceEqual([QueryWorkState.Generating,QueryWorkState.Querying,QueryWorkState.Pending]) &&
            actorProgress.Items[0].Name==primary.Name && actorProgress.Items[2].Name=="judge-fixture",
            "per-model output, waiting and the configured judge are sent as separate shared states");
        var mergeWork = QueryProgressState.Updating(snapshot with { Merging=true },true).WithWork(workSettings,snapshot with { Merging=true });
        Check(mergeWork.Items.Select(i=>i.State).SequenceEqual([QueryWorkState.Complete,QueryWorkState.Complete,QueryWorkState.Merging]),
            "completed providers stay complete while only the judge shows merging");
        var failedWork = QueryProgressState.Finished(snapshot with { MergeFailed=true },true).WithWork(workSettings,snapshot with { MergeFailed=true });
        Check(failedWork.Items[^1].State==QueryWorkState.Failed && failedWork.Items.Take(2).All(i=>i.State==QueryWorkState.Complete) && failedWork.Warning,
            "judge failure keeps successful actor states and remains explicitly incomplete");
        Check(actorProgress.Interrupted("已停止","已保留").Items.All(i=>i.Activity==QueryActivity.Idle) &&
            QueryProgressState.Translating(true).WithWork(workSettings).Items is [{ State:QueryWorkState.Generating }] &&
            QueryProgressState.Translated(true).WithWork(workSettings).Items is [{ State:QueryWorkState.Complete }],
            "cancellation stops actor activity and translation uses only its primary model");
        Check(QueryProgressState.Finished(snapshot,true).WithWork(workSettings,snapshot,false).Items.All(i=>i.Key!="judge") &&
            QueryProgressState.Finished(snapshot,true,true).WithWork(workSettings,snapshot).Items.Any(i=>i.Key=="failure"&&i.State==QueryWorkState.Failed),
            "restored data does not claim the current judge and source failure cannot look fully complete");
        Check(QueryProgressState.Translated(true).Restored && !QueryProgressState.Translated().Restored &&
            mergeWork.Interrupted("已停止","已有结果已保留").Items.Select(i=>i.State).SequenceEqual([QueryWorkState.Complete,QueryWorkState.Complete,QueryWorkState.Failed]),
            "cache restoration is explicit and cancelling a judge preserves completed providers");
        Check(QueryProgressState.Dictionary(true,true).Activity == QueryActivity.Querying &&
            QueryProgressState.Updating(waiting,true).Activity == QueryActivity.Querying &&
            QueryProgressState.Updating(new([new(primary,ModelPhase.Streaming,Entry("new"))],[]),true).Activity == QueryActivity.Generating &&
            QueryProgressState.Updating(snapshot with { Merging=true },true).Activity == QueryActivity.Merging,
            "query motion distinguishes waiting and cached text from newly received output and merging");
        Check(QueryProgressState.Translating().Activity == QueryActivity.Querying &&
            QueryProgressState.Translating(true).Activity == QueryActivity.Generating &&
            QueryProgressState.Translated().Activity == QueryActivity.Idle &&
            QueryProgressState.Translating(true).Interrupted("stopped","retained").Activity == QueryActivity.Idle &&
            QueryProgressState.Finished(snapshot with { MergeFailed=true },true).Activity == QueryActivity.Idle,
            "translation, completion, cancellation and partial failure use the same activity lifecycle");
        var requestId = tracker.Begin(QueryProgressState.Dictionary(true, true));
        tracker.Update(requestId, QueryProgressState.Updating(snapshot with { Merging = true }, true));
        Check(!tracker.Update(requestId, QueryProgressState.Updating(snapshot, true)) && tracker.Current?.Stage == QueryStage.Merge,
            "progress never regresses from merging to supplement");
        var failed = snapshot with { MergeFailed = true, MergeFailure = MergeFailureKind.Timeout };
        tracker.Update(requestId, QueryProgressState.Finished(failed, true));
        Check(!tracker.Update(requestId, QueryProgressState.Updating(snapshot, true)) && tracker.Current is { Busy: false } finished &&
            finished.StageLabels.SequenceEqual(["词典：已结束", "补充：已结束", "合并：未完成", "完成：部分完成"]) &&
            finished.AccessibleName.StartsWith("合并未完成。"), "a terminal state rejects late updates and announces the failed stage");
        Check(QueryProgressState.Translating().StageLabels[0] == "词典：跳过" && QueryProgressState.SourcesFinished(true, false).StageLabels[1] == "补充：跳过",
            "skipped stages are labeled for translation and offline queries");

        var presentation = new QueryPresentation();
        presentation.BeginDictionary("glim", "glim");
        var verb = new AiEntry("glim", true, [new("verb", "act", "动作", "", "", "", [])], "", "unverified",
            WordForms: [new("glim", "verb", [new("presentParticiple", ["glimming"])])]);
        presentation.ApplyAnswers(new([new(primary, ModelPhase.Streaming, verb)], []), null, false);
        Check(!presentation.FormRows.SelectMany(r => r.Cells).Any(c => c.Spelling == "glimming"), "streaming morphology never becomes a visible word form");
        presentation.ApplyAnswers(new([new(primary, ModelPhase.Complete, verb with { Etymology = "合成语源" })], []), null);
        var etymology = presentation.Etymology;
        presentation.ApplyAnswers(new([new(primary, ModelPhase.Complete, verb with { Etymology = "合成语源" })], []), null);
        Check(presentation.FormRows.SelectMany(r => r.Cells).Any(c => c.Spelling == "glimming") && ReferenceEquals(etymology, presentation.Etymology) &&
            etymology is [{ Generated: true, Caption: var caption }] && caption.Contains("未逐项核对"),
            "completed morphology appears and unchanged etymology keeps its identity for renderers");
        var overview = new[] { new ModelAnswer(primary, ModelPhase.Complete, Entry("核心")) };
        presentation.ApplyAnswers(new(overview, SenseMerging.Group(overview)), null, false);
        presentation.ApplyAnswers(new([overview[0] with { Phase = ModelPhase.Pending }], []), null, false);
        Check(presentation.QuickRead is { Visible: true, Text: "核心", State: "缓存预览", ShowState: true },
            "a completed overview stays readable, marked as a cached preview, while a regeneration is pending");
        Check(QueryPresentation.BuildAnswers(new(overview, SenseMerging.Group(overview)), null, []).Originals == OriginalsMode.Collapse &&
            QueryPresentation.BuildAnswers(new([overview[0] with { Phase = ModelPhase.Failed, Entry = null }], []), null, []).Originals == OriginalsMode.Expand &&
            QueryPresentation.BuildAnswers(new([], []), null, []).Empty, "original dictionary text collapses beside definitions and reopens when models fail");
        await ResponsivenessChecks.Run(fixture, root, Check);
        Console.WriteLine($"{count} query session checks passed");
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++) await Task.Delay(10);
        if (!condition()) throw new TimeoutException("FAIL: expected query state was not reached");
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
    }
}
