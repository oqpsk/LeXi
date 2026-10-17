using System.Windows;
using System.Windows.Controls;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    private EtymologyExcerpt[] sourceEtymologies = [];
    private readonly Dictionary<LookupModel, string> modelEtymologies = [];
    internal void ResetLexicalDetails()
    { sourceEtymologies = []; modelEtymologies.Clear(); EtymologyPanel.Children.Clear(); ReferencePanel.Children.Clear(); }
    internal void ApplySourceEtymology(EtymologyExcerpt[] excerpts)
    { sourceEtymologies = excerpts; RenderEtymology(); }
    private void ApplyModelEtymology(LookupSnapshot snapshot)
    {
        foreach (var answer in snapshot.Answers)
        {
            if (answer.Phase == ModelPhase.Streaming) continue;
            if (answer.Entry is { Found: true, Etymology.Length: > 0 } entry) modelEtymologies[answer.Model] = entry.Etymology;
            else if (answer.Phase is ModelPhase.Complete or ModelPhase.Failed && !answer.Cached) modelEtymologies.Remove(answer.Model);
        }
        RenderEtymology();
    }
    private void RenderEtymology()
    {
        EtymologyPanel.Children.Clear();
        if (sourceEtymologies.Length == 0 && modelEtymologies.Count == 0) return;
        var controls = new WrapPanel { Margin = new(0,0,0,8) }; var details = new StackPanel(); var content = new StackPanel();
        foreach (var (model, text) in modelEtymologies.OrderBy(p => p.Key.Provider == "codex" ? 0 : 1))
        {
            content.Children.Add(CompactText(text, 17));
            content.Children.Add(CompactHint(model.Name + " · 模型语源说明，未逐项核对"));
        }
        foreach (var entry in sourceEtymologies)
        {
            content.Children.Add(CompactHint(entry.Label + " · Wiktionary 原文"));
            content.Children.Add(CompactText(entry.Text, 17));
            var link = new Button { Content = "Wiktionary · CC BY-SA 4.0 ↗", FontSize = 14, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Left };
            link.Click += (_, _) => OpenSource(entry.SourceUrl); content.Children.Add(link);
        }
        EtymologyPanel.Children.Add(controls); EtymologyPanel.Children.Add(details);
        AddDisclosure(controls, details, "lookup:etymology", "语源", content);
    }
    private void RenderReferenceBriefs(string query)
    {
        ReferencePanel.Children.Clear();
        var entries = SpecialistReferences.Lookup(query);
        if (entries.Length == 0) return;
        var content = new StackPanel();
        foreach (var entry in entries)
        {
            content.Children.Add(CompactHint(entry.Word + " · " + LexicalLabels.Usage(entry.Domain)));
            if (entry.Expansion.Length > 0) content.Children.Add(CompactText(entry.Expansion, 17));
            content.Children.Add(CompactText(entry.Chinese, 20)); content.Children.Add(CompactText(entry.English, 18));
            var link = new Button { Content = entry.SourceName + " ↗", FontSize = 14, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Left };
            link.Click += (_, _) => OpenSource(entry.SourceUrl); content.Children.Add(link);
        }
        ReferencePanel.Children.Add(new Expander { Header = "专业参考 · 随应用收录", Content = content, IsExpanded = true, Margin = new(0,4,0,8) });
    }
    private void CollapseReferenceBriefs(LookupSnapshot snapshot, OnlineEntry? evidence)
    {
        if (evidence == null || snapshot.Provisional) return;
        var referenceNumbers = evidence.Senses.Select((s, i) => (s, i)).Where(x => x.s.SourceName.Contains("LeXi 摘要")).Select(x => x.i + 1).ToArray();
        if (!referenceNumbers.All(n => snapshot.Groups.Any(g => g.Variants.Any(v => v.Sense.Evidence.Contains(n))))) return;
        if (ReferencePanel.Children.OfType<Expander>().FirstOrDefault() is { Tag: null } brief)
        { brief.IsExpanded = false; brief.Tag = "initially-collapsed"; }
    }
}
