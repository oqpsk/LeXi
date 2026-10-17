using System.Windows;
using System.Windows.Controls;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    private DictionaryCoordinator CreateCoordinator()
    {
        var primary = LookupModel.Primary(settings);
        return new(cache, (model, query, evidence, options, token, progress) => model.Provider == "claude"
            ? (model == primary ? claude : secondaryClaude).LookupAsync(query, evidence, model.Model, token, progress)
            : codex.LookupAsync(query, evidence, options, token), codex.JudgeSensesAsync);
    }

    private static string ModelStatus(LookupSnapshot snapshot) => string.Join(" · ", snapshot.Answers.Select(a => a.Model.Name + (a.Phase switch
    { ModelPhase.Pending => " 查询中", ModelPhase.Streaming => " 生成中", ModelPhase.Complete => a.Cached ? " 缓存" : " 完成", ModelPhase.Failed => " 未完成", _ => a.Cached ? " 缓存" : " 无缓存" })));

    internal void RenderAnswers(LookupSnapshot snapshot, OnlineEntry? evidence, bool finalized = true)
    {
        ApplyModelPronunciation(snapshot);
        AiPanel.Children.Clear();
        if (snapshot.Groups.Length == 0 && snapshot.Answers.All(a => a.Phase == ModelPhase.Offline)) return;
        var stable = finalized && !snapshot.Provisional;
        var countLabel = !stable ? $"预览 · {snapshot.Groups.Length} 条候选" : snapshot.MergeFailed
            ? $"已保留 {snapshot.Groups.Length} 条释义" : $"{snapshot.Groups.Length} 个义项 · {snapshot.Answers.Length} 个模型";
        var card = Card(AiPanel, "双语释义", countLabel, true);
        ((FrameworkElement)card.Children[0]).ToolTip = ModelStatus(snapshot);
        foreach (var (group, i) in snapshot.Groups.Select((g, i) => (g, i)))
            card.Children.Add(SenseRow(group, i, evidence));
        foreach (var answer in snapshot.Answers)
        {
            if (answer.Phase == ModelPhase.Failed) card.Children.Add(CompactHint(answer.Model.Name + "：" + answer.Error + (answer.Cached ? "（已保留缓存）" : ""), true));
            else if (answer.Phase == ModelPhase.Complete && answer.Entry?.Found == false) card.Children.Add(CompactHint(answer.Model.Name + "：" + (string.IsNullOrWhiteSpace(answer.Entry.Note) ? "未返回可靠释义" : answer.Entry.Note)));
        }
        var notes = snapshot.Answers.Where(a => a.Entry?.Found == true && !string.IsNullOrWhiteSpace(a.Entry.Note)).ToArray();
        if (notes.Length > 0)
        {
            var controls = new WrapPanel(); var details = new StackPanel(); var content = new StackPanel();
            foreach (var answer in notes) content.Children.Add(CompactHint(answer.Model.Name + "：" + answer.Entry!.Note));
            card.Children.Add(controls); card.Children.Add(details);
            AddDisclosure(controls, details, "lookup:notes", "补充说明", content);
        }
        if (snapshot.MergeFailed) card.Children.Add(CompactHint(snapshot.MergeNote, true));
        if (snapshot.Groups.Length > 0)
        {
            if (OnlinePanel.Children.OfType<Expander>().FirstOrDefault() is { Tag: null } onlineOriginal)
            { onlineOriginal.IsExpanded = false; onlineOriginal.Tag = "initially-collapsed"; }
            // Preserve every dictionary's original text, with a compact default view.
            // Do this only once per lookup so streaming does not undo user expansion.
            var count = LocalPanel.Children.OfType<Border>().Count();
            if (count > 0)
            {
                var originals = new StackPanel();
                var children = LocalPanel.Children.Cast<UIElement>().ToArray();
                LocalPanel.Children.Clear();
                foreach (var child in children) originals.Children.Add(child);
                LocalPanel.Children.Add(new Expander { Header = $"离线词典原文 · {count} 部", Content = originals, Margin = new(0,4,0,4) });
            }
        }
        else if (!snapshot.Provisional)
        {
            foreach (var original in LocalPanel.Children.OfType<Expander>().Concat(OnlinePanel.Children.OfType<Expander>())) original.IsExpanded = true;
        }
    }
}
