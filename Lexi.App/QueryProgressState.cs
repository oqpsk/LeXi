using Lexi.Core;

namespace Lexi;

internal enum QueryStage { Dictionary, Supplement, Merge, Finished }
internal enum QueryOutcome { Running, Complete, Partial, Failed }

// Only the caller that has awaited the whole query may publish a terminal state.
// A snapshot with two completed models can still be waiting for the sense judge.
internal sealed record QueryProgressState(QueryStage Stage, string Title, string Detail,
    QueryOutcome Outcome = QueryOutcome.Running, bool ModelsEnabled = true, bool MergeEnabled = true, bool Translation = false)
{
    public bool Busy => Outcome == QueryOutcome.Running;
    public bool Warning => Outcome is QueryOutcome.Partial or QueryOutcome.Failed;

    public static QueryProgressState Dictionary(bool models, bool merge) => new(QueryStage.Dictionary, "查询词典中",
        models ? "即时结果先显示，随后补充与整理" : "正在读取词典与已有缓存", ModelsEnabled: models, MergeEnabled: merge);

    public static QueryProgressState Updating(LookupSnapshot snapshot, bool merge)
    {
        if (snapshot.Merging) return new(QueryStage.Merge, "正在合并义项", "正在识别重复释义，最终数量可能减少", MergeEnabled: merge);
        var completed = snapshot.Answers.Count(a => a.Phase == ModelPhase.Complete);
        var failed = snapshot.Answers.Count(a => a.Phase == ModelPhase.Failed);
        var detail = failed > 0 ? $"{completed}/{snapshot.Answers.Length} 个模型已返回 · {failed} 个未完成"
            : $"{completed}/{snapshot.Answers.Length} 个模型已返回 · 义项仍可能增减";
        return new(QueryStage.Supplement, "模型补充中", detail, MergeEnabled: merge);
    }

    public static QueryProgressState Finished(LookupSnapshot snapshot, bool merge, bool sourceIncomplete = false)
    {
        var failed = snapshot.Answers.Count(a => a.Phase == ModelPhase.Failed);
        var count = snapshot.Groups.Length;
        if (snapshot.MergeFailed) return new(QueryStage.Finished, "合并未完成",
            $"已保留 {count} 条释义 · 本次查询已结束", QueryOutcome.Partial, MergeEnabled: merge);
        if (failed > 0) return new(QueryStage.Finished, failed == snapshot.Answers.Length ? "模型补充未完成" : "部分完成",
            "可用结果已保留 · 本次查询已结束", count > 0 ? QueryOutcome.Partial : QueryOutcome.Failed, MergeEnabled: merge);
        if (sourceIncomplete) return new(QueryStage.Finished, "部分来源未能读取",
            "可用结果已保留 · 本次查询已结束", QueryOutcome.Partial, MergeEnabled: merge);
        return new(QueryStage.Finished, count > 0 ? $"已完成 · {count} 个义项" : "查询已结束",
            count > 0 ? "结果已稳定，不再自动增减" : "模型未确认释义，可查看词典原文", QueryOutcome.Complete, MergeEnabled: merge);
    }

    public static QueryProgressState SourcesFinished(bool offline, bool sourceIncomplete) => new(QueryStage.Finished,
        sourceIncomplete ? "部分来源未能读取" : offline ? "离线查询完成" : "词典查询完成",
        "本次查询已结束，内容不再自动变化", sourceIncomplete ? QueryOutcome.Partial : QueryOutcome.Complete, false, false);

    public static QueryProgressState Translating() => new(QueryStage.Supplement, "正在翻译",
        "译文逐步显示，生成中的内容尚未完整", MergeEnabled: false, Translation: true);
    public static QueryProgressState Translated(bool cached = false) => new(QueryStage.Finished, "翻译完成",
        cached ? "已使用完整缓存 · 本次翻译已结束" : "译文已完整，本次翻译已结束", QueryOutcome.Complete, MergeEnabled: false, Translation: true);

    public QueryProgressState Interrupted(string title, string detail) => this with
    { Stage = QueryStage.Finished, Title = title, Detail = detail, Outcome = QueryOutcome.Failed };
}
