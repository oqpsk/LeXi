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

    internal void RenderAnswers(LookupSnapshot snapshot, OnlineEntry? evidence)
    {
        AiPanel.Children.Clear();
        if (snapshot.Groups.Length == 0 && snapshot.Answers.All(a => a.Phase == ModelPhase.Offline)) return;
        var card = Card(AiPanel, "双语释义", snapshot.Answers.Length > 1 ? "双模型" : snapshot.Answers[0].Model.Name, true);
        card.Children.Add(Hint(ModelStatus(snapshot)));
        if (snapshot.Provisional) card.Children.Add(Hint(snapshot.Merging ? "正在判断同义项 · 结果已可阅读" : "生成中 · 义项和来源尚未完整", muted));
        foreach (var (group, i) in snapshot.Groups.Select((g, i) => (g, i)))
        {
            var representative = group.Representative;
            card.Children.Add(new TextBlock
            {
                Text = $"{i + 1:00}  {representative.Sense.PartOfSpeech}" + (string.IsNullOrWhiteSpace(representative.Sense.Register) ? "" : "  ·  " + representative.Sense.Register),
                Foreground = muted, FontSize = 14, Margin = new(0, i == 0 ? 0 : 18, 0, 5)
            });
            card.Children.Add(Hint(string.Join(" + ", group.Variants.Select(v => v.Model.Name).Distinct()) + (group.Variants.Length > 1 ? " · 同义项已合并" : "")));
            RenderSense(card, representative, evidence);
            if (group.Variants.Length > 1)
            {
                var originals = new StackPanel();
                foreach (var variant in group.Variants.Where(v => v.Id != representative.Id))
                {
                    originals.Children.Add(Hint(variant.Model.Name));
                    RenderSense(originals, variant, evidence);
                }
                card.Children.Add(new Expander { Header = "查看另一份表述", Content = originals, Margin = new(0,4,0,8) });
            }
        }
        foreach (var answer in snapshot.Answers)
        {
            if (answer.Phase == ModelPhase.Failed) card.Children.Add(Hint(answer.Model.Name + "：" + answer.Error + (answer.Cached ? "（已保留缓存）" : ""), Theme.Brush("Warning")));
            else if (answer.Phase == ModelPhase.Complete && answer.Entry?.Found == false) card.Children.Add(Hint(answer.Model.Name + "：未返回可靠释义"));
            if (!string.IsNullOrWhiteSpace(answer.Entry?.Note)) card.Children.Add(Hint(answer.Model.Name + "：" + answer.Entry.Note));
        }
        if (!string.IsNullOrEmpty(snapshot.MergeNote)) card.Children.Add(Hint(snapshot.MergeNote));
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
                LocalPanel.Children.Add(new Expander { Header = $"离线词典原文 · {count} 部", Content = originals, Margin = new(0,8,0,12) });
            }
        }
        else if (!snapshot.Provisional)
        {
            foreach (var original in LocalPanel.Children.OfType<Expander>().Concat(OnlinePanel.Children.OfType<Expander>())) original.IsExpanded = true;
        }
    }

    private void RenderSense(Panel parent, SenseCandidate candidate, OnlineEntry? evidence)
    {
        var sense = candidate.Sense;
        var supported = !candidate.Provisional && candidate.EvidenceStatus == "source_based";
        parent.Children.Add(Hint(candidate.Provisional ? "生成中 · 来源待确认" : supported ? "据词典来源整理 · 含 AI 生成内容" : "AI 补充 · 未经词典来源核验", supported || candidate.Provisional ? muted : Theme.Brush("Warning")));
        parent.Children.Add(Selectable(sense.Chinese, 20));
        parent.Children.Add(Selectable(sense.English));
        if (!string.IsNullOrWhiteSpace(sense.Example))
        {
            var example = new StackPanel(); var en = Selectable(sense.Example, 16); en.Foreground = muted; example.Children.Add(en);
            if (!string.IsNullOrWhiteSpace(sense.ExampleChinese)) { var zh = Selectable(sense.ExampleChinese, 16); zh.Foreground = muted; example.Children.Add(zh); }
            parent.Children.Add(new Border { Child = example, BorderBrush = Theme.Brush("Stroke"), BorderThickness = new(2,0,0,0), Padding = new(10,0,0,0), Margin = new(0,8,0,4) });
        }
        if (candidate.Provisional || evidence == null) return;
        foreach (var number in sense.Evidence.Distinct().Where(n => n >= 1 && n <= evidence.Senses.Length))
        {
            var cited = evidence.Senses[number - 1];
            var link = new Button { Content = $"[{number}] {cited.SourceName} ↗", FontSize = 14, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Left, Padding = new(0,3,0,0), ToolTip = cited.Definition };
            link.Click += (_, _) => OpenSource(string.IsNullOrEmpty(cited.SourceUrl) ? evidence.Url : cited.SourceUrl);
            parent.Children.Add(link);
        }
    }
}
