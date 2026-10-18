namespace Lexi.Core;

public enum QueryStage { Dictionary, Supplement, Merge, Finished }
public enum QueryOutcome { Running, Complete, Partial, Failed }
public enum QueryActivity { Idle, Querying, Generating, Merging }

// Only the caller that has awaited the whole query may publish a terminal state.
// A snapshot with two completed models can still be waiting for the sense judge.
public sealed record QueryProgressState(QueryStage Stage, string Title, string Detail,
    QueryOutcome Outcome = QueryOutcome.Running, bool ModelsEnabled = true, bool MergeEnabled = true, bool Translation = false, QueryStage? FailedStage = null, bool OutputStarted = false)
{
    public bool Busy => Outcome == QueryOutcome.Running;
    public bool Warning => Outcome is QueryOutcome.Partial or QueryOutcome.Failed;
    // A visual activity, separate from the stage: waiting for a model is not received output.
    public QueryActivity Activity => !Busy ? QueryActivity.Idle : Stage switch
    {
        QueryStage.Merge => QueryActivity.Merging,
        QueryStage.Supplement when OutputStarted => QueryActivity.Generating,
        _ => QueryActivity.Querying
    };

    public static QueryProgressState Dictionary(bool models, bool merge) => new(QueryStage.Dictionary, "查询词典中",
        models ? "即时结果先显示，随后补充与整理" : "正在读取词典与已有缓存", ModelsEnabled: models, MergeEnabled: merge);

    public static QueryProgressState Updating(LookupSnapshot snapshot, bool merge)
    {
        if (snapshot.Merging) return new(QueryStage.Merge, "正在合并义项", "正在识别重复释义，最终数量可能减少", MergeEnabled: merge);
        var completed = snapshot.Answers.Count(a => a.Phase == ModelPhase.Complete);
        var failed = snapshot.Answers.Count(a => a.Phase == ModelPhase.Failed);
        var detail = failed > 0 ? $"{completed}/{snapshot.Answers.Length} 个模型已返回 · {failed} 个未完成"
            : $"{completed}/{snapshot.Answers.Length} 个模型已返回 · 义项仍可能增减";
        return new(QueryStage.Supplement, "模型补充中", $"预览 · {snapshot.Groups.Length} 条候选 · " + detail, MergeEnabled: merge,
            OutputStarted: snapshot.Answers.Any(a => !a.Cached && a.Phase is ModelPhase.Streaming or ModelPhase.Complete && a.Entry?.Senses.Length > 0));
    }

    public static QueryProgressState Finished(LookupSnapshot snapshot, bool merge, bool sourceIncomplete = false, int referenceCount = 0)
    {
        var failed = snapshot.Answers.Count(a => a.Phase == ModelPhase.Failed);
        var count = snapshot.Groups.Length + referenceCount;
        if (snapshot.MergeFailed) return new(QueryStage.Finished, "合并未完成",
            MergeFailures.Describe(snapshot.MergeFailure) + $"。已保留 {count} 条释义；已停止整理，不会继续等待。可点击底部“重试未完成”，仅重试出错步骤。",
            QueryOutcome.Partial, MergeEnabled: merge, FailedStage: QueryStage.Merge);
        if (failed > 0) return new(QueryStage.Finished, failed == snapshot.Answers.Length ? "模型补充未完成" : "部分完成",
            "可用结果已保留 · 本次查询已结束", count > 0 ? QueryOutcome.Partial : QueryOutcome.Failed, MergeEnabled: merge, FailedStage: QueryStage.Supplement);
        if (sourceIncomplete) return new(QueryStage.Finished, "部分来源未能读取",
            "可用结果已保留 · 本次查询已结束", QueryOutcome.Partial, MergeEnabled: merge, FailedStage: QueryStage.Dictionary);
        return new(QueryStage.Finished, count > 0 ? $"已完成 · {count} 个义项" : "查询已结束",
            count > 0 ? "结果已稳定，不再自动增减" : "模型未确认释义，可查看词典原文", QueryOutcome.Complete, MergeEnabled: merge);
    }

    public static QueryProgressState SourcesFinished(bool offline, bool sourceIncomplete) => new(QueryStage.Finished,
        sourceIncomplete ? "部分来源未能读取" : offline ? "离线查询完成" : "词典查询完成",
        "本次查询已结束，内容不再自动变化", sourceIncomplete ? QueryOutcome.Partial : QueryOutcome.Complete, false, false);

    public static QueryProgressState Translating(bool outputStarted = false) => new(QueryStage.Supplement, "正在翻译",
        "译文逐步显示，生成中的内容尚未完整", MergeEnabled: false, Translation: true, OutputStarted: outputStarted);
    public static QueryProgressState Translated(bool cached = false) => new(QueryStage.Finished, "翻译完成",
        cached ? "已使用完整缓存 · 本次翻译已结束" : "译文已完整，本次翻译已结束", QueryOutcome.Complete, MergeEnabled: false, Translation: true);

    public QueryProgressState Interrupted(string title, string detail) => this with
    { Stage = QueryStage.Finished, Title = title, Detail = detail, Outcome = QueryOutcome.Failed };

    // Stage details shown in the status-ring popover and announced to assistive technology.
    public string[] StageLabels
    {
        get
        {
            var names = new[] { "词典", Translation ? "翻译" : "补充", "合并", "完成" };
            var labels = new string[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                bool skipped = i == 0 && Translation || i == 1 && !ModelsEnabled || i == 2 && !MergeEnabled;
                bool active = i == (int)Stage;
                labels[i] = names[i] + "：" + (skipped ? "跳过" : FailedStage == (QueryStage)i ? "未完成" :
                    active ? Busy ? "进行中" : Outcome == QueryOutcome.Partial ? "部分完成" : Warning ? "已停止" : "完成" :
                    i < (int)Stage ? "已结束" : "等待");
            }
            return labels;
        }
    }
    public string AccessibleName => Title + "。" + Detail + "。" + string.Join("，", StageLabels);
}

// One lifecycle per accepted query. Late or regressing updates cannot revive a finished query.
public sealed class QueryProgressTracker
{
    private long generation;
    public long Generation => generation;
    public QueryProgressState? Current { get; private set; }
    public long Begin(QueryProgressState state) { generation++; Current = null; Update(generation, state); return generation; }
    public void Reset() { generation++; Current = null; }
    public bool Update(long request, QueryProgressState state)
    {
        if (request != generation || Current is { Busy: false } || Current is { } previous && state.Stage < previous.Stage) return false;
        if (state == Current) return false;
        Current = state; return true;
    }
}
