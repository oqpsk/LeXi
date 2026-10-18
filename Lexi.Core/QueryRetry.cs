namespace Lexi.Core;

[Flags] public enum SourceFailures { None = 0, Offline = 1, Online = 2 }

// Data management waits for the current query; the message is shown as is.
public sealed class DataBusyException() : InvalidOperationException("请等待查询结束。");

public sealed partial class QuerySession
{
    private sealed record RetryState(string Query, LocalHit[] Local, OnlineEntry? Online, OnlineEntry? Evidence,
        LookupSnapshot Result, AppSettings Options, bool SourceIncomplete, SourceFailures FailedSources = SourceFailures.None, string? SavedIdentity = null)
    {
        // A saved checkpoint keeps only this hash of the settings that shape a query, not a settings copy.
        public string Identity => SavedIdentity ?? RetryIdentity(Options);
    }
    private static string RetryIdentity(AppSettings settings) => Cache.Hash(QueryIdentity(settings));
    private RetryState? retry;
    private bool maintaining;
    public async Task<T> ManageDataAsync<T>(Func<Task<T>> action)
    {
        lock (gate) { if (maintaining || historyPending > 0 || progress.Current?.Busy == true) throw new DataBusyException(); maintaining = true; }
        try { return await action(); }
        finally { lock (gate) maintaining = false; }
    }
    public bool CanRetry => kind != QueryViewKind.Home && !maintaining && Settings.Online && progress.Current?.Busy != true &&
        (contextWork?.State == QueryWorkState.Failed && CanPickContext(pageContext) || kind == QueryViewKind.Translation && Presentation.Translation is { Complete: false } ||
        retry is { } r && (r.SourceIncomplete || r.Result.MergeFailed || r.Result.Answers.Any(a => a.Phase == ModelPhase.Failed)));

    // A context failure does not invalidate either model answer or its successful merge.
    private async Task RetryContextAsync()
    {
        string query, sentence;
        LookupSnapshot? snapshot;
        RetryState? saved;
        long id, progressId; CancellationToken token;
        var timing = new LookupTiming(clock);
        lock (gate)
        {
            if (contextRetryIdentity != RetryIdentity(Settings))
            { status = "模型设置已更改，请点击重新生成；已有结果已保留。"; Publish(); return; }
            query = current; sentence = pageContext; snapshot = Presentation.Snapshot; saved = retry;
            if (snapshot == null || !CanPickContext(sentence)) return;
            (id, token) = Begin(QueryViewKind.Dictionary, query, input);
            retry = saved; activeTiming = timing; timing.MarkFirst();
            progressId = progress.Begin(QueryProgressState.Finished(snapshot, Settings.MergeSenses, saved?.SourceIncomplete == true)
                .WithWork(Settings, snapshot, false) with { Stage = QueryStage.Merge, Outcome = QueryOutcome.Running,
                    Title = "正在重试本句语境", Detail = "已保留释义和整理结果，仅重试语境判断" });
            status = "仅重试本句语境判断…"; Publish();
        }
        try
        {
            await ApplyContextAsync(id, query, sentence, token);
            token.ThrowIfCancellationRequested();
            Step(id, () =>
            {
                progress.Update(progressId, QueryProgressState.Finished(snapshot, Settings.MergeSenses, saved?.SourceIncomplete == true).WithWork(Settings, snapshot, false));
                status = timing.Finish(ContextStatus("重试结束 · 已保留成功步骤"));
            });
        }
        catch (OperationCanceledException)
        { Step(id, () => progress.Update(progressId, (progress.Current ?? QueryProgressState.Dictionary(true, true)).Interrupted("语境重试已停止", "一般释义已保留"))); }
    }

    public async Task RetryFailedAsync()
    {
        RetryState? saved;
        bool contextOnly;
        lock (gate)
        {
            if (!CanRetry) return;
            saved = retry;
            contextOnly = contextWork?.State == QueryWorkState.Failed && (saved == null ||
                !saved.SourceIncomplete && !saved.Result.MergeFailed && saved.Result.Answers.All(a => a.Phase != ModelPhase.Failed));
        }
        if (contextOnly) { await RetryContextAsync(); return; }
        if (saved == null) { await RegenerateAsync(); return; }
        // A changed configuration requires explicit regeneration; a retry never changes providers.
        if (saved.Identity != RetryIdentity(Settings))
        { SetStatus("模型设置已更改，请点击重新生成；已有结果已保留。"); return; }
        // The query settings are unchanged, so the current copy serves live and restored checkpoints alike.
        saved = saved with { Options = Settings.ForModel(LookupModel.Primary(Settings)), SavedIdentity = null };
        var timing = new LookupTiming(clock);
        long id, progressId; CancellationToken token;
        lock (gate)
        {
            if (maintaining) return;
            (id, token) = Begin(QueryViewKind.Dictionary, saved.Query, saved.Query);
            retry = saved;
            activeTiming = timing;
            Presentation.ContextFailed();
            progressId = progress.Begin(QueryProgressState.Dictionary(true, saved.Options.MergeSenses).WithWork(saved.Options));
            status = "仅重试未完成步骤…"; Publish();
        }
        try
        {
            var result = saved.Result;
            var online = saved.Online;
            var local = saved.Local;
            var failedSources = saved.FailedSources;
            if (failedSources.HasFlag(SourceFailures.Offline))
            {
                try
                {
                    var dictionary = await Task.Run(() => new OfflineLibrary(Paths.DataRoot), token);
                    token.ThrowIfCancellationRequested();
                    local = dictionary.Lookup(saved.Query, saved.Options);
                    var forms = dictionary.Forms(saved.Query, saved.Options);
                    if (!Step(id, () => { if (Library.IsCompletedSuccessfully) retiredLibraries.Add(Library.Result); Library = Task.FromResult(dictionary); Presentation.ShowLocal(local, saved.Query, saved.Options.OfflineEnabled); Presentation.ApplyLocalWordForms(saved.Query, forms); })) dictionary.Dispose();
                    failedSources &= ~SourceFailures.Offline;
                }
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { }
            }
            if (failedSources.HasFlag(SourceFailures.Online))
            {
                online = await FetchSourceAsync(saved.Query, token) ?? online;
                if (!sourceFetchFailed.ContainsKey(id)) failedSources &= ~SourceFailures.Online;
                if (online != null) Step(id, () => Presentation.ShowOnline(online, false));
            }
            var stage = saved with { Local = local, Online = online, FailedSources = failedSources, SourceIncomplete = failedSources != SourceFailures.None };
            Step(id, () => retry = stage);
            if (saved.Options.MergeSenses && result.MergeFailed || result.Answers.Any(a => a.Phase == ModelPhase.Failed))
            {
                var coordinator = new DictionaryCoordinator(Cache, providers.Define, providers.Judge);
                var report = Reporter<LookupSnapshot>(s => Step(id, () =>
                { retry = stage with { Result = s }; Presentation.ApplyAnswers(s, saved.Evidence, false); progress.Update(progressId, QueryProgressState.Updating(s, saved.Options.MergeSenses).WithWork(saved.Options, s)); }));
                result = await Task.Run(() => coordinator.LookupAsync(saved.Query, saved.Evidence, saved.Options, true, token, report, saved.Result), token);
            }
            token.ThrowIfCancellationRequested();
            var incomplete = failedSources != SourceFailures.None;
            var finished = saved with { Local = local, Online = online, Result = result, SourceIncomplete = incomplete, FailedSources = failedSources };
            Step(id, () => { retry = finished; Presentation.ApplyAnswers(result, saved.Evidence); });
            await SaveDictionaryAsync(saved.Query, local, online, saved.Evidence, result, saved.Options.MergeSenses, incomplete, token);
            timing.MarkFirst();
            await ApplyContextAsync(id, saved.Query, string.IsNullOrWhiteSpace(pageContext) ? null : pageContext, token);
            token.ThrowIfCancellationRequested();
            Step(id, () => { progress.Update(progressId, QueryProgressState.Finished(result, saved.Options.MergeSenses, incomplete).WithWork(saved.Options, result)); status = timing.Finish(ContextStatus("重试结束 · 已保留成功步骤")); });
        }
        catch (Exception e) when (DictionaryCoordinator.ExpectedFailure(e))
        { Step(id, () => { retry = saved; status = "重试未完成，已有结果已保留。"; progress.Update(progressId, (progress.Current ?? QueryProgressState.Dictionary(true, true).WithWork(saved.Options)).Interrupted("重试未完成", "仅未完成步骤可重试")); }); }
        catch (OperationCanceledException) { Step(id, () => {
            var answers = (retry ?? saved).Result.Answers.Select(a => a.Phase is ModelPhase.Pending or ModelPhase.Streaming
                ? new ModelAnswer(a.Model, ModelPhase.Failed, Error: "已停止") : a).ToArray();
            var mergingStopped = (retry ?? saved).Result.Merging || (retry ?? saved).Result.MergeFailed;
            retry = (retry ?? saved) with { Result = new(answers, SenseMerging.Group(answers), MergeFailed: mergingStopped, MergeFailure: mergingStopped ? MergeFailureKind.Cancelled : null) };
            Presentation.ApplyAnswers(retry.Result, saved.Evidence);
            progress.Update(progressId, (progress.Current ?? QueryProgressState.Dictionary(true, true).WithWork(saved.Options)).Interrupted("重试已停止", "已有结果已保留，可继续重试")); }); }
    }
}
