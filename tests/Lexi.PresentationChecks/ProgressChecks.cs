using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Lexi;
using Lexi.Core;

internal static partial class Program
{
    private static void CheckQueryProgress(App app, MainWindow main)
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
        var view = (QueryProgressView)main.FindName("QueryProgress");
        var scroll = (ScrollViewer)main.FindName("ResultsScroll");
        Check(view.Visibility == Visibility.Hidden && view.Parent is StackPanel && !Contents(scroll).Contains(view) && !Theme.CanDragFrom(view),
            "compact progress reserves space beside the query controls and never starts a window drag");
        var primary = new LookupModel("codex", "gpt-6.1-sol"); var secondary = new LookupModel("claude", "haiku");
        AiSense Sense(int i) => new("noun", "Synthetic meaning " + i, "合成释义 " + i, "standard", "Example.", "例句。", []);
        AiEntry Entry(params int[] values) => new("fixture", true, values.Select(Sense).ToArray(), "", "unverified");
        var first = new[] { new ModelAnswer(primary, ModelPhase.Streaming, Entry(1, 2)), new ModelAnswer(secondary, ModelPhase.Pending) };
        var complete = new[] { new ModelAnswer(primary, ModelPhase.Complete, Entry(1, 2, 3)), new ModelAnswer(secondary, ModelPhase.Complete, Entry(4, 5, 6)) };
        var two = new LookupSnapshot(first, SenseMerging.Group(first));
        var six = new LookupSnapshot(complete, SenseMerging.Group(complete));
        var four = new LookupSnapshot(complete, SenseMerging.Group(complete,
            new("fixture", [new("m0s0", "m1s0", "equivalent", 0.99), new("m0s1", "m1s1", "equivalent", 0.99)])), MergeNote: "同义项已整理");
        string[] CardLabels() => Contents((Panel)main.FindName("AiPanel"), true).OfType<TextBlock>().Select(t => t.Text).ToArray();
        var request = view.Begin(QueryProgressState.Dictionary(true, true));
        Check(view.Current is { Busy: true, Stage: QueryStage.Dictionary }, "dictionary reads start a visible busy lifecycle");
        main.RenderAnswers(two, null, false); view.Update(request, QueryProgressState.Updating(two, true));
        Check(view.Current is { Busy: true, Stage: QueryStage.Supplement } && CardLabels().Contains("预览 · 2 条候选"),
            "two early senses are labeled as preview candidates");
        main.RenderAnswers(six, null, false); view.Update(request, QueryProgressState.Updating(six, true));
        Check(!six.Provisional && view.Current is { Busy: true, Stage: QueryStage.Supplement } && CardLabels().Contains("预览 · 6 条候选"),
            "both models completing does not prematurely finalize six candidates");
        view.Update(request, QueryProgressState.Updating(six with { Merging = true }, true));
        Check(view.Current is { Busy: true, Stage: QueryStage.Merge } && view.Current.Detail.Contains("可能减少"),
            "merge stage explicitly explains why the displayed count can shrink");
        main.RenderAnswers(four, null); view.Update(request, QueryProgressState.Finished(four, true));
        Check(view.Current is { Busy: false, Outcome: QueryOutcome.Complete } && view.Current.Title.Contains("4 个义项") &&
            CardLabels().Contains("4 个义项") && AutomationProperties.GetName(view).Contains("结果已稳定"),
            "only the final return marks four stable senses and announces completion");
        view.Update(request, QueryProgressState.Updating(two, true));
        Check(view.Current?.Busy == false, "late streaming callbacks cannot revive a finished query");
        var next = view.Begin(QueryProgressState.Translating());
        view.Update(request, QueryProgressState.Finished(four, true));
        view.Update(request, QueryProgressState.Dictionary(true, true).Interrupted("已取消", "old query"));
        Check(view.Current is { Busy: true, Translation: true }, "a previous query's completion or cancellation cannot overwrite the new query");
        view.Update(next, QueryProgressState.Translated());
        Check(view.Current is { Busy: false, Translation: true, MergeEnabled: false }, "translation has its own generating-to-complete lifecycle");
        next = view.Begin(QueryProgressState.Translating()); view.Update(next, QueryProgressState.Translated(true));
        Check(view.Current is { Busy: false } && view.Current.Detail.Contains("完整缓存"), "a complete cached translation settles immediately");
        var failedMerge = six with { MergeFailed = true, MergeNote = "同义项判断未完成，已保留全部释义" };
        next = view.Begin(QueryProgressState.Dictionary(true, true));
        main.RenderAnswers(failedMerge, null); view.Update(next, QueryProgressState.Finished(failedMerge, true));
        Check(view.Current is { Busy: false, Warning: true } && view.Current.Detail.Contains("保留 6 条") &&
            CardLabels().Contains("已保留 6 条释义"), "judge failure ends loading without presenting an unmerged list as successfully merged");
        var oneFailed = new LookupSnapshot([complete[0], new(secondary, ModelPhase.Failed)], SenseMerging.Group([complete[0]]));
        var allFailed = new LookupSnapshot([new(primary, ModelPhase.Failed), new(secondary, ModelPhase.Failed)], []);
        Check(QueryProgressState.Finished(oneFailed, true) is { Busy: false, Outcome: QueryOutcome.Partial } &&
            QueryProgressState.Finished(allFailed, true) is { Busy: false, Outcome: QueryOutcome.Failed },
            "one or both model failures terminate with distinct incomplete outcomes");
        Check(QueryProgressState.SourcesFinished(true, false) is { Busy: false, ModelsEnabled: false, MergeEnabled: false } &&
            QueryProgressState.SourcesFinished(false, false) is { Busy: false, ModelsEnabled: false } &&
            QueryProgressState.SourcesFinished(false, true) is { Busy: false, Warning: true },
            "offline, AI-disabled and source-error exits all stop loading and skip unused stages");
        Check(QueryProgressState.Finished(new([complete[0]], SenseMerging.Group([complete[0]])), false) is { Busy: false, MergeEnabled: false } &&
            QueryProgressState.Finished(new([new(primary, ModelPhase.Complete, Entry())], []), false) is { Busy: false },
            "single-model and no-sense results both settle without waiting for a merge");
        next = view.Begin(QueryProgressState.Translating());
        view.Update(next, QueryProgressState.Translating().Interrupted("翻译未完成", "本次翻译已结束，可重试"));
        Check(view.Current is { Busy: false, Outcome: QueryOutcome.Failed }, "translation failure or cancellation terminates the busy indicator");
        var states = new[] { QueryProgressState.Dictionary(true, true), QueryProgressState.Updating(two, true),
            QueryProgressState.Updating(six with { Merging = true }, true), QueryProgressState.Finished(four, true),
            QueryProgressState.Finished(failedMerge, true), QueryProgressState.Finished(allFailed, true),
            QueryProgressState.SourcesFinished(true, false), QueryProgressState.Translating(), QueryProgressState.Translated(true) };
        var fits = true;
        foreach (var state in states)
        {
            view.Begin(state); Measure((FrameworkElement)main.Content, 558, 398);
            var tooltip = (ToolTip)view.ToolTip;
            fits &= Contents(tooltip).OfType<TextBlock>().Any(t => t.Text.Contains(state.Detail)) &&
                view.Content is FrameworkElement glyph && glyph.Width == 18 && glyph.Height == 18 &&
                view.ActualHeight <= 32 && view.ActualWidth <= 32 && scroll.ActualHeight > 200;
        }
        Check(fits, "status ring fits the query row and retains full stage information in its tooltip");
        var accent = ((SolidColorBrush)app.FindResource("Accent")).Color;
        var selection = ((SolidColorBrush)app.FindResource("Selection")).Color;
        var ink = ((SolidColorBrush)app.FindResource("Ink")).Color;
        Check(accent.R > accent.B && accent.B > accent.G && selection.R > selection.B && selection.B > selection.G &&
            Contrast(ink, selection) >= 4.5 && Contrast(accent, ((SolidColorBrush)app.FindResource("Surface")).Color) >= 4.5,
            "pink accent and rose selection retain readable foreground contrast");
        Check(AutomationProperties.GetLiveSetting(view) == AutomationLiveSetting.Polite &&
            states.All(s => !s.Detail.Contains('%') && !s.Detail.Contains("秒")), "progress offers polite stage announcements without fabricated percentages or estimates");
        Console.WriteLine($"{count} query-progress checks passed without displaying windows");
    }
}
