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
        (kind == QueryViewKind.Translation && Presentation.Translation is { Complete: false } ||
        retry is { } r && (r.SourceIncomplete || r.Result.MergeFailed || r.Result.Answers.Any(a => a.Phase == ModelPhase.Failed)));

    public async Task RetryFailedAsync()
    {
        RetryState? saved;
        lock (gate) { if (!CanRetry) return; saved = retry; }
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
            progressId = progress.Begin(QueryProgressState.Dictionary(true, saved.Options.MergeSenses));
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
                { retry = stage with { Result = s }; Presentation.ApplyAnswers(s, saved.Evidence, false); progress.Update(progressId, QueryProgressState.Updating(s, saved.Options.MergeSenses)); }));
                result = await Task.Run(() => coordinator.LookupAsync(saved.Query, saved.Evidence, saved.Options, true, token, report, saved.Result), token);
            }
            token.ThrowIfCancellationRequested();
            var incomplete = failedSources != SourceFailures.None;
            var finished = saved with { Local = local, Online = online, Result = result, SourceIncomplete = incomplete, FailedSources = failedSources };
            Step(id, () => { retry = finished; Presentation.ApplyAnswers(result, saved.Evidence); });
            await SaveDictionaryAsync(saved.Query, local, online, saved.Evidence, result, saved.Options.MergeSenses, incomplete, token);
            timing.MarkFirst();
            Step(id, () => { progress.Update(progressId, QueryProgressState.Finished(result, saved.Options.MergeSenses, incomplete)); status = timing.Finish("重试结束 · 已保留成功步骤"); });
        }
        catch (Exception e) when (DictionaryCoordinator.ExpectedFailure(e))
        { Step(id, () => { retry = saved; status = "重试未完成，已有结果已保留。"; progress.Update(progressId, QueryProgressState.Dictionary(true, true).Interrupted("重试未完成", "仅未完成步骤可重试")); }); }
        catch (OperationCanceledException) { Step(id, () => {
            var answers = (retry ?? saved).Result.Answers.Select(a => a.Phase is ModelPhase.Pending or ModelPhase.Streaming
                ? new ModelAnswer(a.Model, ModelPhase.Failed, Error: "已停止") : a).ToArray();
            var mergingStopped = (retry ?? saved).Result.Merging || (retry ?? saved).Result.MergeFailed;
            retry = (retry ?? saved) with { Result = new(answers, SenseMerging.Group(answers), MergeFailed: mergingStopped, MergeFailure: mergingStopped ? MergeFailureKind.Cancelled : null) };
            Presentation.ApplyAnswers(retry.Result, saved.Evidence);
            progress.Update(progressId, QueryProgressState.Dictionary(true, true).Interrupted("重试已停止", "已有结果已保留，可继续重试")); }); }
    }
}
