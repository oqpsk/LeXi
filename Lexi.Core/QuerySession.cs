using System.Text.Json;

namespace Lexi.Core;

public sealed record QueryProviders(
    Func<LookupModel, string, OnlineEntry?, AppSettings, CancellationToken, IProgress<AiEntry>, Task<AiEntry>> Define,
    Func<SenseMergeRequest, AppSettings, CancellationToken, Task<SenseMergePlan>> Judge,
    Func<string, string, AppSettings, CancellationToken, IProgress<TranslationEntry>, Task<TranslationEntry>> Translate,
    // Which sense a word has in the sentence it was selected in; absent in checks that do not need it.
    Func<ContextRequest, AppSettings, CancellationToken, Task<ContextPick>>? Context = null);

// Settings that change query results; hashed into retry checkpoints, so names and order stay fixed.
internal sealed record QuerySettingsKey(bool Online, bool AutoAi, bool OfflineEnabled, string[]? DisabledDictionaries, string CodexPath,
    string Provider, string Model, string SecondaryProvider, string SecondaryModel, bool ParallelLookup,
    bool MergeSenses, string JudgeProvider, string JudgeModel, string CompatibleBaseUrl,
    bool CompatibleStreaming, InferenceProfile[]? InferenceProfiles, string ReasoningEffort, string ServiceTier);

// The query lifecycle shared by every front end: routing, sources, caches, models, saved views,
// history and navigation. Each step publishes a complete QueryView; front ends only render it.
public sealed partial class QuerySession : IDisposable
{
    private readonly object gate = new();
    private readonly OnlineDictionary web;
    private readonly Pronunciation pronunciations;
    private readonly CodexDictionary codex;
    private readonly ClaudeDictionary claude, secondaryClaude;
    private readonly ApiDictionary api;
    private readonly QueryProviders providers;
    private readonly CompatibleTextClient compatibleClient;
    private readonly bool ownsCompatibleClient;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CompatibleCredentials> compatibleCredentials = new();
    private readonly bool warmClients;
    private readonly CancellationTokenSource catalogStop = new();
    private int catalogStarted;
    private readonly QueryProgressTracker progress = new();
    private readonly TimeProvider clock;
    private CancellationTokenSource? lookup;
    private long request, revision;
    private QueryViewKind kind = QueryViewKind.Home;
    private string current = "", input = "", speechText = "", status = "正在准备词库…";
    private bool disposed;
    private readonly object historyGate = new();
    private int historyPending;
    private readonly List<OfflineLibrary> retiredLibraries = [];
    // The sentence of the current dictionary page's query, kept for regeneration.
    private string pageContext = "";
    // The current query's stage times; each published view may mark the TL;DR or first sense.
    private LookupTiming? activeTiming;
    private readonly TimingLog timings;

    public QuerySession(LexiPaths paths, Task<OfflineLibrary>? library = null, QueryProviders? providers = null,
        HttpMessageHandler? network = null, TimeProvider? clock = null,
        Func<string, CompatibleCredentials>? textCredentials = null, CompatibleTextClient? textClient = null)
    {
        Paths = paths;
        Cache = new(paths.UserRoot);
        Settings = AppSettings.Load(paths.UserRoot);
        History = new(paths.UserRoot);
        timings = new(paths.UserRoot);
        Library = library ?? Task.Run(() => new OfflineLibrary(paths.DataRoot));
        web = new(network); pronunciations = new(network);
        codex = new(paths.AssetRoot, paths.UserRoot);
        claude = new(paths.AssetRoot, paths.UserRoot); secondaryClaude = new(paths.AssetRoot, paths.UserRoot);
        compatibleClient = textClient ?? new(); ownsCompatibleClient = textClient == null;
        var compatible = new CompatibleDictionary(paths.AssetRoot, compatibleClient, textCredentials ?? (url =>
            compatibleCredentials.GetValueOrDefault(CompatibleText.NormalizeBase(url)) ?? new()), paths.UserRoot);
        warmClients = providers == null;
        api = new(paths.AssetRoot, network);
        // Each model goes to its own service: the Codex or Claude Code subscription, the configured
        // text endpoint, or an API with the user's key.
        this.providers = providers ?? new((model, query, evidence, options, token, preview) => model.Provider switch
            {
                CompatibleText.Provider => compatible.LookupAsync(query, evidence, options, token, preview),
                "claude" => (model == LookupModel.Primary(Settings) ? claude : secondaryClaude).LookupAsync(query, evidence, options, token, preview),
                "openai" or "anthropic" => api.LookupAsync(model, query, evidence, Keys.For(model.Provider), token),
                _ => codex.LookupAsync(query, evidence, options, token)
            },
            (request, options, token) => options.Provider == CompatibleText.Provider ? compatible.JudgeAsync(request, options, token)
                : ModelCatalog.IsApi(options.Provider) ? api.JudgeSensesAsync(LookupModel.Primary(options), request, Keys.For(options.Provider), token)
                : codex.JudgeSensesAsync(request, options, token),
            (text, target, options, token, preview) => options.Provider switch
            {
                CompatibleText.Provider => compatible.TranslateAsync(text, target, options, token, preview),
                "claude" => claude.TranslateAsync(text, target, options, token, preview),
                "openai" or "anthropic" => api.TranslateAsync(LookupModel.Primary(options), text, target, Keys.For(options.Provider), token),
                _ => codex.TranslateAsync(text, target, options, token)
            },
            (request, options, token) => options.Provider == CompatibleText.Provider ? compatible.ContextAsync(request, options, token)
                : ModelCatalog.IsApi(options.Provider) ? api.ContextAsync(LookupModel.Primary(options), request, Keys.For(options.Provider), token)
                : codex.ContextAsync(request, options, token));
        this.clock = clock ?? TimeProvider.System;
        View = Build();
    }

    public LexiPaths Paths { get; }
    public AppSettings Settings { get; }
    public Cache Cache { get; }
    // API keys for this session only, from the front end's secure storage; never saved by Core.
    public ApiKeys Keys { get; } = new();
    public SavedQueries Saved => new(Cache);
    public QueryHistory History { get; }
    public QueryNavigation Navigation { get; } = new();
    public Task<OfflineLibrary> Library { get; private set; }
    public QueryPresentation Presentation { get; } = new();
    public string Current { get { lock (gate) return current; } }
    public QueryView View { get; private set; }
    // Raised in order for every published view; front ends marshal to their UI thread if needed.
    public event Action<QueryView>? Changed;

    public void SetTextCredentials(string baseUrl, CompatibleCredentials credentials) => compatibleCredentials[CompatibleText.NormalizeBase(baseUrl)] = credentials;
    private string ProviderName => Settings.Provider switch
    { CompatibleText.Provider => "自定义文本服务", "claude" or "anthropic" => "Claude", "openai" => "OpenAI", _ => "Codex" };

    // Completes when this start's background read of the OAuth model catalog ends (or was skipped).
    public Task SubscriptionCatalog { get; private set; } = Task.CompletedTask;

    public async Task InitializeAsync()
    {
        RefreshSubscriptionCatalog();
        UpdatePreparedModel();
        try { await Library; UpdateReadyStatus(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { lock (gate) { status = "离线词库未能加载，请在设置中检查词库状态。"; Publish(); } }
    }

    public string ReadyStatus() => !Settings.OfflineEnabled ? "离线词典已停用 · 可在设置中启用" :
        Library.IsCompletedSuccessfully && Library.Result.Dictionaries.Any(d => d.Available && !(Settings.DisabledDictionaries ?? []).Contains(d.Id)) ?
            "准备就绪" : "暂无可用离线词库 · 可在设置中检查";
    public void UpdateReadyStatus() { lock (gate) { if (current.Length > 0) return; status = ReadyStatus(); Publish(); } }
    public void SetStatus(string text) { lock (gate) { status = text; Publish(); } }

    public string[] Suggest(string text) => History.Suggest(text)
        .Concat(Settings.OfflineEnabled && Library.IsCompletedSuccessfully ? Library.Result.Suggest(text, Settings) : [])
        .Distinct(StringComparer.Ordinal).Take(10).ToArray();

    public Task<InputPreviewView?> PreviewAsync(string text, CancellationToken token = default) =>
        InputPreview.ReadAsync(text, Saved, Library, Settings, token);

    public void Cancel() { lock (gate) lookup?.Cancel(); }

    // ---- Settings ----
    public bool SaveSettings()
    {
        try { Settings.Save(Paths.UserRoot); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus("设置暂时无法保存，请检查文件夹写入权限。"); return false; }
    }
    // Window-closing preferences do not invalidate dictionary or model results.
    public string QuerySettingsIdentity() => QueryIdentity(Settings);
    private static string QueryIdentity(AppSettings settings) => JsonSerializer.Serialize(new QuerySettingsKey(
        settings.Online, settings.AutoAi, settings.OfflineEnabled, settings.DisabledDictionaries, settings.CodexPath,
        settings.Provider, settings.Model, settings.SecondaryProvider, settings.SecondaryModel, settings.ParallelLookup,
        settings.MergeSenses, settings.JudgeProvider, settings.JudgeModel, settings.CompatibleBaseUrl,
        settings.CompatibleStreaming, settings.InferenceProfiles, settings.ReasoningEffort, settings.ServiceTier), Json.Options);
    public void SettingsSaved(string previousIdentity)
    {
        SaveSettings(); UpdateReadyStatus();
        if (previousIdentity == QuerySettingsIdentity()) return;
        StopForSettings("查询设置已保存"); UpdatePreparedModel();
    }
    public void SetOnline(bool online)
    {
        Settings.Online = online; SaveSettings();
        StopForSettings("查询模式已更改"); UpdatePreparedModel();
        UpdateReadyStatus(); RefreshSubscriptionCatalog();
    }

    // OAuth models come from the official clients. Read their catalog once per start, in the
    // background, so every subscription model is offered before the connection page is opened.
    // A failure keeps the cached catalog and the saved choices; nothing is selected for the user.
    private void RefreshSubscriptionCatalog()
    {
        // Where the subscription clients cannot run there is no OAuth catalog to read.
        if (!warmClients || ModelCatalog.ApiOnly || !Settings.Online || Interlocked.Exchange(ref catalogStarted, 1) == 1) return;
        var (path, root, token) = (Settings.CodexPath, Paths.UserRoot, catalogStop.Token);
        SubscriptionCatalog = Task.Run(async () =>
        {
            try { await InferenceOptions.RememberSubscriptions(root, await InferenceOptions.ReadSubscriptions(path, token), token); }
            catch (Exception e) when (e is InvalidOperationException or TimeoutException or OperationCanceledException or IOException or
                UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
        });
    }
    // The kept result is marked before cancellation, so a cancelled flow that resumes inline
    // (no UI synchronization context) cannot describe itself as an interrupted query.
    private void StopForSettings(string message)
    {
        lock (gate)
        {
            if (current.Length > 0) KeepCurrentResult(message);
            lookup?.Cancel();
        }
    }
    public void KeepCurrentResult(string message)
    {
        lock (gate)
        {
            if (progress.Current is { Busy: true } running) progress.Begin(running.Interrupted("查询已停止", "设置已更改 · 点击重新生成可更新"));
            status = message + " · 已保留当前结果" + (Settings.Online ? " · 重新生成可更新" : "");
            Publish();
        }
    }
    public void UpdatePreparedModel()
    {
        if (!warmClients) return;
        if (Settings.Online && Settings.AutoAi && Settings.Provider == "claude") claude.Prepare(Settings.ForModel(LookupModel.Primary(Settings)));
        else claude.ReleasePrepared();
        var second = LookupModel.Secondary(Settings);
        if (Settings.Online && Settings.AutoAi && Settings.ParallelLookup && second.Provider == "claude" && second != LookupModel.Primary(Settings)) secondaryClaude.Prepare(Settings.ForModel(second));
        else secondaryClaude.ReleasePrepared();
    }

    // ---- Navigation and home ----
    public QueryPage? Move(int direction, double scroll = 0)
    {
        lock (gate)
        {
            Navigation.RememberScroll(scroll);
            var page = Navigation.Move(direction);
            if (page != null) Publish();
            return page;
        }
    }
    public async Task<QueryPage?> NavigateAsync(int direction, double scroll = 0)
    {
        if (Move(direction, scroll) is not { } page) return null;
        if (page.Query.Length == 0) ShowHome(recordVisit: false);
        else await SubmitAsync(page.Query, navigating: true);
        return page;
    }
    public void ShowHome(bool recordVisit = true, double scroll = 0)
    {
        lock (gate)
        {
            if (recordVisit) { Navigation.RememberScroll(scroll); Navigation.Visit(""); }
            request++; lookup?.Cancel(); retry = null;
            kind = QueryViewKind.Home; current = ""; input = ""; speechText = "";
            Presentation.ResetLexical(); Presentation.SetQuery("");
            progress.Reset();
            status = ReadyStatus();
            Publish();
        }
    }
    public bool RemoveHistory(HistoryEntry entry) { lock (gate) if (maintaining) return false; bool removed; lock (historyGate) removed = History.Remove(entry); lock (gate) Publish(); return removed; }

    // ---- Queries ----
    public Task RegenerateAsync()
    {
        string query, context; lock (gate) { query = current; context = pageContext; }
        return query.Length > 0 ? SubmitAsync(query, true, context: context) : Task.CompletedTask;
    }

    // context: the sentence the query was selected in (LookupContext), when the front end has one.
    // recordHistory: false reopens a page the reader already had open (iOS after the system closed the app):
    // it becomes the current page with home behind it, and history keeps its order.
    public async Task SubmitAsync(string raw, bool force = false, bool navigating = false, double scroll = 0, string? context = null, bool recordHistory = true)
    {
        lock (gate) { if (maintaining) return; pageContext = navigating || string.IsNullOrWhiteSpace(context) ? "" : context.Trim(); }
        var validation = Queries.Validate(raw);
        if (validation != null) { SetStatus(validation); return; }
        var timing = new LookupTiming(clock);
        var knownTerm = raw.Length <= 100 && !raw.Contains('\n') && Library.IsCompletedSuccessfully &&
            Library.Result.Lookup(Queries.Normalize(raw), Settings).Length > 0;
        var savedDictionary = Saved.Dictionary(Queries.LookupKey(raw));
        var savedTranslation = Saved.TranslationResult(raw.Trim(), Translation.Target(raw.Trim()));
        var translating = navigating ? Navigation.Current.Translation : savedTranslation != null || savedDictionary == null && Queries.IsText(raw, knownTerm);
        Task historyWrite = Task.CompletedTask;
        var recording = !navigating && recordHistory;
        if (!navigating)
        {
            lock (gate) { Navigation.RememberScroll(scroll); Navigation.Visit(raw.Trim(), translating); if (recording) historyPending++; }
            var submittedAt = clock.GetUtcNow();
            if (recording) historyWrite = Task.Run(() => { lock (historyGate) History.Record(raw, translating, submittedAt); });
        }
        var sentence = navigating || string.IsNullOrWhiteSpace(context) ? null : context.Trim();
        try
        {
            if (translating) await TranslateAsync(raw.Trim(), force, timing);
            else await LookupAsync(raw, force, timing, sentence);
        }
        finally { try { await historyWrite; } finally { if (recording) lock (gate) historyPending--; } }
        if (timing.Total != null)
        {
            var models = timing.Path is "models" or "translation" ? (translating ? [LookupModel.Primary(Settings)] : LookupModel.Selected(Settings))
                .Select(m => m.Provider + "/" + m.Model).ToArray() : [];
            var record = timing.Record(translating ? "translation" : "dictionary", models, sentence != null, clock.GetUtcNow());
            await Task.Run(() => timings.Append(record));
        }

    }

    private (long Id, CancellationToken Token) Begin(QueryViewKind next, string query, string text)
    {
        // Advance first: a cancelled flow resuming inline must already be stale.
        request++; lookup?.Cancel(); lookup?.Dispose(); lookup = new(); retry = null; sourceFetchFailed.Clear(); activeTiming = null;
        kind = next; current = query; input = text; speechText = text;
        return (request, lookup.Token);
    }

    private async Task LookupAsync(string raw, bool force, LookupTiming timing, string? context = null)
    {
        var query = Queries.LookupKey(raw);
        var modelsEnabled = Settings.Online && (Settings.AutoAi || force);
        var mergeEnabled = modelsEnabled && Settings.MergeSenses;
        var initialProgress = QueryProgressState.Dictionary(modelsEnabled, mergeEnabled).WithWork(Settings);
        long id, progressId; CancellationToken token;
        lock (gate)
        {
            if (maintaining) return;
            (id, token) = Begin(QueryViewKind.Dictionary, query, raw.Trim());
            activeTiming = timing;
            Presentation.BeginDictionary(query, raw.Trim());
            status = "查询本地词库…";
            progressId = progress.Begin(initialProgress);
            Publish();
        }
        var notes = new List<string>();
        var sourceIncomplete = false;
        var failedSources = SourceFailures.None;
        LocalHit[] local = [];
        Task pronunciationTask = Task.CompletedTask;
        Task<OnlineEntry?>? earlySource = null;
        Task? contextPick = null;
        var earlyWatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (Saved.Dictionary(query, force) is { } saved)
            { timing.Path = "saved"; Step(id, () => RestoreSavedCore(saved, timing, progressId)); await ApplyContextAsync(id, query, context, token); return; }
            try
            {
                if (Settings.OfflineEnabled)
                {
                    var dictionary = await Library;
                    token.ThrowIfCancellationRequested();
                    local = dictionary.Lookup(query, Settings);
                    var forms = dictionary.Forms(query, Settings);
                    Step(id, () => Presentation.ApplyLocalWordForms(query, forms));
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { notes.Add("本地词库不可用"); sourceIncomplete = true; failedSources |= SourceFailures.Offline; }
            token.ThrowIfCancellationRequested();
            Step(id, () => { Presentation.ShowLocal(local, query, Settings.OfflineEnabled); BeginOverviewCore(query, local); });
            timing.Mark("local");
            if (local.Any(h => !string.IsNullOrWhiteSpace(h.Entry.Chinese + h.Entry.English + h.Entry.Original)) ||
                SpecialistReferences.Lookup(query).Length > 0) timing.MarkFirst();
            // Read existing pronunciation/etymology before deciding whether any network work is needed.
            pronunciationTask = LoadPronunciationAsync(query, id, token, force, allowNetwork: false);
            var online = Cache.Read<OnlineEntry>("online-v3", query);
            if (online == null && query == Queries.Normalize(query)) online = Cache.Read<OnlineEntry>("online-v2", query);
            if (online is { } cachedOnline) { Step(id, () => Presentation.ShowOnline(cachedOnline, true)); timing.Mark("source"); if (online.Senses.Length > 0) timing.MarkFirst(); }
            var evidence = DictionaryEvidence.Combine(query, local, online, Presentation.SourceEtymologies);
            var coordinator = new DictionaryCoordinator(Cache, providers.Define, providers.Judge);
            var cachedAnswers = coordinator.Cached(query, evidence, Settings);
            if (cachedAnswers.Groups.Length > 0) { var shown = cachedAnswers; var basis = evidence; Step(id, () => Presentation.ApplyAnswers(shown, basis, !Settings.Online)); timing.MarkFirst(); }
            if (!force && (cachedAnswers.Answers.Any(a => a.Entry != null) || online != null && !Settings.AutoAi))
            {
                var shown = cachedAnswers; var basis = evidence;
                Step(id, () => Presentation.ApplyAnswers(shown, basis));
                await SaveDictionaryAsync(query, local, online, evidence, cachedAnswers, Settings.MergeSenses, sourceIncomplete, token);
                token.ThrowIfCancellationRequested(); timing.MarkFirst(); timing.Path = "cached";
                Step(id, () =>
                {
                    progress.Update(progressId, QueryProgressState.Finished(shown, Settings.MergeSenses, sourceIncomplete).WithWork(Settings, shown, false) with { Restored = true, Detail = "已恢复已有缓存 · 点击重新生成可更新" });
                    status = timing.Finish("已显示缓存 · 重新生成可更新");
                });
                await ApplyContextAsync(id, query, context, token);
                return;
            }
            if (!Settings.Online)
            {
                await pronunciationTask; token.ThrowIfCancellationRequested();
                await SaveDictionaryAsync(query, local, online, evidence, cachedAnswers, false, sourceIncomplete, token);
                token.ThrowIfCancellationRequested();
                timing.Path = "offline";
                Step(id, () => { status = timing.Finish("离线模式 · 使用本地词库与已缓存释义"); progress.Update(progressId, QueryProgressState.SourcesFinished(true, sourceIncomplete).WithWork(Settings)); });
                return;
            }
            pronunciationTask = LoadPronunciationAsync(query, id, token, force);
            var fresh = !force && online != null;
            if (!fresh && (force || !Queries.HasChinese(query) || !local.Any(h => h.Dictionary.AllowAiEvidence)))
            {
                Step(id, () => status = "本地查询完成 · 正在查在线词典…");
                try
                {
                    earlyWatch.Restart(); earlySource ??= FetchSourceAsync(query, token);
                    var budget = TimeSpan.FromMilliseconds(1000) - earlyWatch.Elapsed;
                    if (!earlySource.IsCompleted && budget > TimeSpan.Zero)
                        await Task.WhenAny(earlySource, Task.Delay(budget, token));
                    var fetched = !modelsEnabled || earlySource.IsCompleted ? await earlySource : null;
                    token.ThrowIfCancellationRequested();
                    if (fetched != null) { online = fetched; await TryCacheAsync("online-v3", query, online); }
                    else if (online == null && earlySource.IsCompleted && !sourceFetchFailed.ContainsKey(id)) notes.Add("在线词典未收录");
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { notes.Add("在线查询超时"); sourceIncomplete = true; }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException) { notes.Add("在线来源暂不可用"); sourceIncomplete = true; }
            }
            if (sourceFetchFailed.ContainsKey(id)) { sourceIncomplete = true; failedSources |= SourceFailures.Online; }
            token.ThrowIfCancellationRequested();
            var onlineEntry = online;
            Step(id, () =>
            {
                if (onlineEntry != null) Presentation.ShowOnline(onlineEntry, fresh || clock.GetUtcNow() - onlineEntry.FetchedAt > TimeSpan.FromMinutes(1));
                else Presentation.ShowNoOnline(local.Length == 0);
            });
            timing.Mark("source");
            evidence = DictionaryEvidence.Combine(query, local, online, Presentation.SourceEtymologies);
            cachedAnswers = coordinator.Cached(query, evidence, Settings);
            var sourceEvidence = evidence; var sourceAnswers = cachedAnswers;
            Step(id, () => Presentation.ApplyAnswers(sourceAnswers, sourceEvidence, !modelsEnabled));
            if (cachedAnswers.Groups.Length > 0 || online?.Senses.Length > 0) timing.MarkFirst();
            if (!modelsEnabled)
            {
                await pronunciationTask; token.ThrowIfCancellationRequested();
                Step(id, () => retry = new(query, local, online, evidence, cachedAnswers, Settings.ForModel(LookupModel.Primary(Settings)), sourceIncomplete, failedSources));
                await SaveDictionaryAsync(query, local, online, evidence, cachedAnswers, false, sourceIncomplete, token);
                token.ThrowIfCancellationRequested();
                timing.Path = "sources";
                Step(id, () =>
                {
                    status = timing.Finish(Status(notes, "查询完成 · 点击“重新生成”获取双语释义"));
                    progress.Update(progressId, QueryProgressState.SourcesFinished(false, sourceIncomplete).WithWork(Settings));
                });
                return;
            }
            Step(id, () => progress.Update(progressId, QueryProgressState.Updating(new(LookupModel.Selected(Settings)
                .Select(m => new ModelAnswer(m, ModelPhase.Pending)).ToArray(), []), mergeEnabled).WithWork(Settings)));
            var finished = false;
            try
            {
                var report = Reporter<LookupSnapshot>(snapshot =>
                {
                    lock (gate)
                    {
                        if (finished || token.IsCancellationRequested || id != request) return;
                        Presentation.ApplyAnswers(snapshot, sourceEvidence, false);
                        if (snapshot.Groups.Length > 0) timing.MarkFirst();
                        foreach (var answer in snapshot.Answers)
                        {
                            if (answer.Phase == ModelPhase.Streaming) timing.Mark("stream:" + answer.Model.Provider + "/" + answer.Model.Model);
                            else if (answer.Phase is ModelPhase.Complete or ModelPhase.Failed) timing.Mark("model:" + answer.Model.Provider + "/" + answer.Model.Model);
                        }
                        progress.Update(progressId, QueryProgressState.Updating(snapshot, mergeEnabled).WithWork(Settings, snapshot));
                        status = snapshot.Merging ? "释义已显示 · 正在合并同义项…" : ModelStatus(snapshot);
                        Publish();
                        // Every model has answered: which sense the sentence means no longer waits for the merge.
                        if (context != null && contextPick == null && !snapshot.Answers.Any(a => a.Phase is ModelPhase.Pending or ModelPhase.Streaming))
                            contextPick = Task.Run(() => ApplyContextAsync(id, query, context, token));
                    }
                });
                var options = Settings.ForModel(LookupModel.Primary(Settings));
                // Buffered SSE and CLI output can complete reads synchronously for a whole burst.
                // Keep parsing, grouping and provider callbacks off the front end's UI context.
                var result = await Task.Run(() => coordinator.LookupAsync(query, evidence, options, force, token, report), token);
                token.ThrowIfCancellationRequested();
                lock (gate) finished = true;
                if (result.MergeNote.Length > 0 || result.MergeFailed) timing.Mark("merge");
                Step(id, () => Presentation.ApplyAnswers(result, sourceEvidence));
                if (result.Groups.Length > 0) timing.MarkFirst();
                await pronunciationTask;
                if (earlySource != null)
                {
                    var late = await earlySource;
                    if (late != null) { online = late; Step(id, () => Presentation.ShowOnline(late, false)); }
                    else if (sourceFetchFailed.ContainsKey(id)) { sourceIncomplete = true; failedSources |= SourceFailures.Online; }
                }
                token.ThrowIfCancellationRequested();
                Step(id, () => retry = new(query, local, online, evidence, result, options, sourceIncomplete, failedSources));
                await SaveDictionaryAsync(query, local, online, evidence, result, mergeEnabled, sourceIncomplete, token);
                token.ThrowIfCancellationRequested();
                var failures = result.Answers.Count(a => a.Phase == ModelPhase.Failed);
                var outcome = failures > 0 ? $"{failures} 个模型未完成 · 其余结果已保留" : "查询完成";
                if (!string.IsNullOrEmpty(result.MergeNote)) outcome += " · " + result.MergeNote;
                timing.Path = "models";
                Step(id, () =>
                {
                    progress.Update(progressId, QueryProgressState.Finished(result, mergeEnabled, sourceIncomplete,
                        SpecialistReferences.Uncovered(query, result.Groups, sourceEvidence).Length).WithWork(options, result));
                    status = timing.Finish(Status(notes, outcome));
                });
            }
            finally { lock (gate) finished = true; }
            await (contextPick ?? ApplyContextAsync(id, query, context, token));
        }
        catch (OperationCanceledException)
        { Step(id, () => progress.Update(progressId, (progress.Current ?? initialProgress).Interrupted("查询已中断", "本次查询已结束，可重新查询"))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (token.IsCancellationRequested) return;
            try { await pronunciationTask; }
            catch (Exception pronunciationError) when (DictionaryCoordinator.ExpectedFailure(pronunciationError) || pronunciationError is OperationCanceledException) { }
            if (token.IsCancellationRequested) return;
            Step(id, () =>
            {
                status = timing.Finish("读取失败，请检查词库文件是否完整。");
                progress.Update(progressId, (progress.Current ?? initialProgress).Interrupted("查询未完成", "读取失败 · 本次查询已结束，可重试"));
            });
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, bool> sourceFetchFailed = new();
    private async Task<OnlineEntry?> FetchSourceAsync(string query, CancellationToken token)
    {
        long id; lock (gate) id = request;
        try
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token); stop.CancelAfter(TimeSpan.FromSeconds(8));
            var entry = await web.LookupAsync(query, stop.Token);
            if (entry != null && !token.IsCancellationRequested) await TryCacheAsync("online-v3", query, entry);
            return entry;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or InvalidDataException or OperationCanceledException or TimeoutException)
        { if (!token.IsCancellationRequested) sourceFetchFailed[id] = true; return null; }
    }

    private async Task TranslateAsync(string text, bool force, LookupTiming timing)
    {
        var target = Translation.Target(text);
        var key = Translation.Key(text, target, Settings);
        var initialProgress = QueryProgressState.Translating().WithWork(Settings);
        long id, progressId; CancellationToken token;
        lock (gate)
        {
            if (maintaining) return;
            (id, token) = Begin(QueryViewKind.Translation, text, text);
            activeTiming = timing;
            Presentation.BeginTranslation(text, target);
            progressId = progress.Begin(initialProgress);
            Publish();
        }
        if (Saved.TranslationResult(text, target, force) is { } saved)
        {
            timing.MarkFirst();
            Step(id, () =>
            {
                Presentation.ShowTranslation(saved.Entry, true, false, saved.Model.Name);
                timing.Path = "cached"; status = timing.Finish("已显示缓存翻译 · 重新生成可更新", "首段译文");
                progress.Update(progressId, QueryProgressState.Translated(true).WithWork(Settings));
            });
            return;
        }
        var cached = Cache.Read<TranslationEnvelope>("translations-v1", key);
        if (cached != null && !force)
        {
            try
            {
                var validated = Translation.Parse(JsonSerializer.Serialize(cached.Entry, Json.Options), text, target);
                timing.MarkFirst();
                Step(id, () => { Presentation.ShowTranslation(validated, true, false, ProviderName); timing.Path = "cached"; status = timing.Finish("已显示缓存翻译", "首段译文"); });
                await SaveTranslationAsync(text, target, validated, LookupModel.Primary(Settings), token);
                if (token.IsCancellationRequested) return;
                Step(id, () => progress.Update(progressId, QueryProgressState.Translated(true).WithWork(Settings)));
                return;
            }
            catch (InvalidDataException) { }
        }
        if (!Settings.Online)
        {
            Step(id, () =>
            {
                timing.Path = "offline"; status = timing.Finish("离线模式下没有这段文本的缓存翻译。开启在线补充后可翻译。", "首段译文");
                progress.Update(progressId, initialProgress.Interrupted("没有缓存翻译", "本次查询已结束 · 开启在线补充后可翻译"));
            });
            return;
        }
        Step(id, () => status = $"{ProviderName} 正在翻译…");
        var finished = false;
        string? lastPreview = null;
        try
        {
            var report = Reporter<TranslationEntry>(partial =>
            {
                lock (gate)
                {
                    if (finished || token.IsCancellationRequested || id != request) return;
                    var fingerprint = string.Join("\n\n", partial.Paragraphs);
                    if (fingerprint == lastPreview) return;
                    lastPreview = fingerprint;
                    Presentation.ShowTranslation(partial, false, true, ProviderName); timing.MarkFirst(); status = "翻译生成中…";
                    progress.Update(progressId, QueryProgressState.Translating(partial.Paragraphs.Any(p => !string.IsNullOrWhiteSpace(p))).WithWork(Settings));
                    Publish();
                }
            });
            var options = Settings.ForModel(LookupModel.Primary(Settings));
            var entry = await Task.Run(() => providers.Translate(text, target, options, token, report), token);
            token.ThrowIfCancellationRequested();
            lock (gate) finished = true;
            timing.MarkFirst();
            Step(id, () => Presentation.ShowTranslation(entry, false, false, ProviderName));
            await TryCacheAsync("translations-v1", key, new TranslationEnvelope(entry, clock.GetUtcNow()));
            token.ThrowIfCancellationRequested();
            await SaveTranslationAsync(text, target, entry, LookupModel.Primary(Settings), token);
            token.ThrowIfCancellationRequested();
            Step(id, () => { timing.Path = "translation"; status = timing.Finish("翻译完成", "首段译文"); progress.Update(progressId, QueryProgressState.Translated().WithWork(Settings)); });
        }
        catch (OperationCanceledException)
        { Step(id, () => progress.Update(progressId, initialProgress.Interrupted("翻译已中断", "本次翻译已结束，可重新翻译"))); }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or InvalidDataException or JsonException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            if (!token.IsCancellationRequested)
                Step(id, () =>
                {
                    Presentation.ShowTranslationError(ex.Message);
                    timing.Path = "translation"; status = timing.Finish("翻译未完成，可重试", "首段译文");
                    progress.Update(progressId, initialProgress.Interrupted("翻译未完成", "本次翻译已结束，可重试"));
                });
        }
        finally { lock (gate) finished = true; }
    }

    // ---- Presentation steps also used directly by front-end checks ----
    public void ApplyAnswers(LookupSnapshot snapshot, OnlineEntry? evidence, bool finalized = true) =>
        Update(() => Presentation.ApplyAnswers(snapshot, evidence, finalized));
    public void BeginOverview(string query, LocalHit[] local) => Update(() => BeginOverviewCore(query, local));
    private void BeginOverviewCore(string query, LocalHit[] local)
    {
        Presentation.BeginOverview(local);
        Presentation.ApplyAnswers(new([], []), DictionaryEvidence.Combine(query, local, null), !Settings.Online);
    }
    public void ApplyLocalWordForms(string query, WordFormSet[] entries) => Update(() => Presentation.ApplyLocalWordForms(query, entries));
    public void ApplyOnlineWordForms(string query, WordFormSet[] entries) => Update(() => Presentation.ApplyOnlineWordForms(query, entries));
    public void ApplyModelWordForms(string query, LookupSnapshot snapshot) => Update(() => Presentation.ApplyModelWordForms(query, snapshot));
    public void ApplySourceEtymology(EtymologyExcerpt[] excerpts) => Update(() => Presentation.ApplySourceEtymology(excerpts));
    public void ResetLexical() => Update(Presentation.ResetLexical);
    public void ResetWordForms() => Update(Presentation.ResetWordForms);
    public QueryProgressState RestoreSaved(SavedDictionary saved, LookupTiming timing)
    {
        lock (gate) { var state = RestoreSavedCore(saved, timing, progress.Generation); Publish(); return state; }
    }
    private void Update(Action action) { lock (gate) { action(); Publish(); } }

    private QueryProgressState RestoreSavedCore(SavedDictionary saved, LookupTiming timing, long progressId)
    {
        var query = saved.Query;
        current = query; Presentation.SetQuery(query);
        Presentation.ApplyLocalWordForms(query, saved.LocalForms); Presentation.ApplyOnlineWordForms(query, saved.OnlineForms);
        Presentation.ShowLocal(saved.Local, query, Settings.OfflineEnabled); BeginOverviewCore(query, saved.Local);
        Presentation.ApplySourceEtymology(saved.Etymologies);
        Presentation.RestorePronunciation(saved.Pronunciation);
        if (saved.Online != null) Presentation.ShowOnline(saved.Online, true);
        Presentation.ApplyAnswers(saved.Result, saved.Evidence);
        retry = saved.RetryIdentity == null ? null : new(query, saved.Local, saved.Online, saved.Evidence, saved.Result,
            Settings.ForModel(LookupModel.Primary(Settings)), saved.SourceIncomplete, saved.FailedSources, saved.RetryIdentity);
        timing.MarkFirst();
        var state = (saved.Result.Answers.Length > 0 ? QueryProgressState.Finished(saved.Result, saved.MergeEnabled,
            saved.SourceIncomplete, SpecialistReferences.Uncovered(query, saved.Result.Groups, saved.Evidence).Length) :
            QueryProgressState.SourcesFinished(false, saved.SourceIncomplete)).WithWork(Settings, saved.Result, false) with { Restored = true, Detail = "已恢复保存的结果 · 点击重新生成可更新" };
        progress.Update(progressId, state);
        status = timing.Finish(state.Warning ? "已显示缓存 · 部分结果 · 重新生成可重试" : "已显示缓存 · 重新生成可更新");
        return state;
    }

    public Task LoadPronunciationAsync(string query, CancellationToken token, bool force, bool allowNetwork = true)
    { long id; lock (gate) id = request; return LoadPronunciationAsync(query, id, token, force, allowNetwork); }

    private async Task LoadPronunciationAsync(string query, long id, CancellationToken token, bool force, bool allowNetwork = true)
    {
        if (Queries.HasChinese(query)) return;
        var cached = Cache.Read<PronunciationEntry>("pronunciations-v3", query);
        void Apply(PronunciationEntry entry)
        {
            lock (gate)
            {
                if (token.IsCancellationRequested || current != query || id != request) return;
                Presentation.ApplySourcePronunciation(query, entry);
                Publish();
            }
        }
        if (cached?.Word == query) Apply(cached);
        else if (Cache.Read<PronunciationEntry>("pronunciations-v2", query) is { } legacy && legacy.Word == query) Apply(legacy);
        if (!allowNetwork || !Settings.Online || !force && cached?.Word == query) return;
        try
        {
            var entry = await pronunciations.LookupAsync(query, token);
            if (entry == null) return;
            token.ThrowIfCancellationRequested(); Apply(entry);
            await TryCacheAsync("pronunciations-v3", query, entry);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException) { }
    }

    private async Task SaveDictionaryAsync(string query, LocalHit[] local, OnlineEntry? online, OnlineEntry? evidence,
        LookupSnapshot result, bool merge, bool incomplete, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        SavedDictionary saved;
        lock (gate) { if (current != query || kind != QueryViewKind.Dictionary || token.IsCancellationRequested) return; saved = new(query, local, Presentation.LocalWordForms, Presentation.OnlineWordForms, Presentation.SourceEtymologies,
            online, evidence, result, Presentation.Pronunciation, merge, incomplete, clock.GetUtcNow(), RetryIdentity: retry?.Identity, FailedSources: retry?.FailedSources ?? SourceFailures.None); }
        try { await Saved.SaveDictionaryAsync(saved, token); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Keep the displayed result if persistence is unavailable. */ }
    }

    private async Task SaveTranslationAsync(string text, string target, TranslationEntry entry, LookupModel model, CancellationToken token)
    {
        try { await Saved.SaveTranslationAsync(text, target, new(entry, model, clock.GetUtcNow()), token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // The selection's sentence picks one of the shown senses, through the sense-judge model, once the
    // entry is final. It changes only the TL;DR and the matching sense's label; a failure keeps both.
    private async Task ApplyContextAsync(long id, string query, string? sentence, CancellationToken token)
    {
        if (sentence == null || providers.Context == null || !Settings.Online || !Settings.AutoAi || token.IsCancellationRequested) return;
        ContextRequest? contextRequest;
        lock (gate) { if (id != request) return; contextRequest = LookupContext.Request(query, sentence, Presentation.Snapshot); }
        if (contextRequest == null) return;
        var judge = ModelCatalog.Judge(Settings);
        var options = Settings.ForModel(judge);
        var key = contextRequest.InputHash + ":" + judge.CacheKey("", options);
        if (Cache.Read<ContextPick>(LookupContext.CacheKind, key) is { } saved)
        {
            try { var valid = LookupContext.Parse(JsonSerializer.Serialize(saved, Json.Options), contextRequest); Step(id, () => { Presentation.ApplyContext(sentence, valid); activeTiming?.Mark("context"); }); return; }
            catch (Exception e) when (e is InvalidDataException or JsonException) { }
        }
        Step(id, () => Presentation.BeginContext(sentence));
        try
        {
            var pick = await Task.Run(() => providers.Context(contextRequest, options, token), token);
            pick = LookupContext.Parse(JsonSerializer.Serialize(pick, Json.Options), contextRequest);
            token.ThrowIfCancellationRequested();
            await TryCacheAsync(LookupContext.CacheKind, key, pick);
            Step(id, () => { Presentation.ApplyContext(sentence, pick); activeTiming?.Mark("context"); });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) when (DictionaryCoordinator.ExpectedFailure(e) || e is OperationCanceledException)
        { Step(id, () => Presentation.ContextFailed()); }
    }

    private async Task TryCacheAsync<T>(string kindName, string key, T value)
    { try { await Cache.WriteAsync(kindName, key, value); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Cache failure never discards a valid result. */ } }

    public static string Status(List<string> notes, string fallback) => string.Join(" · ", new[] { fallback }.Concat(notes));
    public static string ModelStatus(LookupSnapshot snapshot) => string.Join(" · ", snapshot.Answers.Select(a => a.Model.Name + (a.Phase switch
    { ModelPhase.Pending => " 查询中", ModelPhase.Streaming => " 生成中", ModelPhase.Complete => a.Cached ? " 缓存" : " 完成", ModelPhase.Failed => " 未完成", _ => a.Cached ? " 缓存" : " 无缓存" })));

    // Process ordered model snapshots on the generation worker. Front ends marshal complete
    // views; posting every token to their synchronization context would starve input/cancel.
    private static IProgress<T> Reporter<T>(Action<T> handler) => new InlineProgress<T>(handler);

    private bool Step(long id, Action action)
    {
        lock (gate) { if (id != request || disposed) return false; action(); Publish(); return true; }
    }

    private void Publish()
    {
        if (activeTiming is { } timing && kind == QueryViewKind.Dictionary)
        {
            if (Presentation.QuickRead is { Visible: true, Text.Length: > 0 }) timing.Mark("tldr");
            if (Presentation.Answers?.Senses.Length > 0) timing.Mark("sense");
        }
        View = Build(); Changed?.Invoke(View);
    }

    private QueryView Build() => new(request, ++revision, kind, current, input, speechText, Presentation.Heading,
        Presentation.Pronunciation, progress.Current, progress.Generation, status, kind != QueryViewKind.Home && Settings.Online,
        new(Navigation.CanBack, Navigation.CanForward, QueryNavigation.Label(Navigation.Back), QueryNavigation.Label(Navigation.Forward),
            Navigation.Current.Id, Navigation.Back?.Id ?? -1, Navigation.Forward?.Id ?? -1),
        Presentation.Local, Presentation.LocalHint, Presentation.Online, Presentation.OnlineHint, Presentation.Answers,
        Presentation.QuickRead, Presentation.FormRows, Presentation.Etymology, Presentation.Translation, CanRetry);

    public void Dispose()
    {
        lock (gate) { disposed = true; lookup?.Cancel(); }
        catalogStop.Cancel();
        claude.Dispose(); secondaryClaude.Dispose(); compatibleCredentials.Clear(); if (ownsCompatibleClient) compatibleClient.Dispose(); web.Dispose(); pronunciations.Dispose();
        if (Library.IsCompletedSuccessfully) Library.Result.Dispose();
        foreach (var library in retiredLibraries) library.Dispose();
    }
}
