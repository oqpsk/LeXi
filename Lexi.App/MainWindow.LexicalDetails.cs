using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    private EtymologyExcerpt[] sourceEtymologies = [];
    private readonly Dictionary<LookupModel, string> modelEtymologies = [];
    private string etymologyIdentity = "";
    private LocalHit[] overviewLocal = [];
    private bool overviewFromModel;
    internal void ResetLexicalDetails()
    {
        EtymologyPopup.IsOpen = false; EtymologyButton.IsChecked = false;
        EtymologyButton.Visibility = Visibility.Collapsed; EtymologyContent.Children.Clear();
        sourceEtymologies = []; modelEtymologies.Clear(); etymologyIdentity = "";
        overviewLocal = []; overviewFromModel = false; QuickReadCard.Visibility = Visibility.Collapsed; QuickReadText.Text = "";
    }
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
        var identity = System.Text.Json.JsonSerializer.Serialize(new { sourceEtymologies, Models = modelEtymologies.ToArray() }, Json.Options);
        if (identity == etymologyIdentity) return;
        etymologyIdentity = identity;
        var offset = EtymologyScroll.VerticalOffset;
        EtymologyContent.Children.Clear();
        EtymologyButton.Visibility = sourceEtymologies.Length > 0 || modelEtymologies.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (EtymologyButton.Visibility == Visibility.Collapsed) EtymologyPopup.IsOpen = false;
        foreach (var (model, text) in modelEtymologies.OrderBy(p => p.Key.Provider == "codex" ? 0 : 1))
        {
            EtymologyContent.Children.Add(CompactText(text, 17));
            EtymologyContent.Children.Add(CompactHint(model.Name + " · 模型语源说明，未逐项核对"));
        }
        foreach (var entry in sourceEtymologies)
        {
            EtymologyContent.Children.Add(CompactHint(entry.Label + " · Wiktionary 原文"));
            EtymologyContent.Children.Add(CompactText(entry.Text, 17));
            var link = new Button { Content = "Wiktionary · CC BY-SA 4.0 ↗", FontSize = 14, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Left };
            link.Click += (_, _) => OpenSource(entry.SourceUrl); EtymologyContent.Children.Add(link);
        }
        EtymologyScroll.ScrollToVerticalOffset(offset);
    }
    private void Etymology_Opened(object sender, EventArgs e)
    {
        EtymologySurface.Width = Math.Clamp(ActualWidth - 64, 280, 460);
        EtymologyScroll.MaxHeight = Math.Clamp(ActualHeight - 150, 140, 420);
        CloseEtymology.Focus();
    }
    private void Etymology_Close(object sender, RoutedEventArgs e) => DismissEtymology();
    internal void DismissEtymology()
    { EtymologyPopup.IsOpen = false; EtymologyButton.IsChecked = false; EtymologyButton.Focus(); }
    private void Etymology_KeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Escape) { DismissEtymology(); e.Handled = true; } }

    internal void BeginLexicalOverview(string query, LocalHit[] local)
    {
        overviewLocal = local;
        RenderAnswers(new([], []), DictionaryEvidence.Combine(query, local, null), !settings.Online);
    }
    private void RenderQuickRead(LookupSnapshot snapshot, SpecialistReference[] uncovered, bool finalized)
    {
        var generated = snapshot.Groups.Any(g => g.Variants.Any(v => !v.Provisional));
        if (!generated && overviewFromModel && !finalized && snapshot.Provisional)
        { QuickReadLabel.Text = "TL;DR · 缓存预览"; return; }
        var meanings = QuickRead.Create(snapshot.Groups, uncovered, overviewLocal);
        QuickReadCard.Visibility = meanings.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        overviewFromModel = generated && meanings.Length > 0;
        if (meanings.Length == 0) return;
        var state = !generated ? "词典摘录" : !finalized || snapshot.Provisional ? "预览" :
            snapshot.MergeFailed || snapshot.Answers.Any(a => a.Phase == ModelPhase.Failed) ? "部分结果" : "已整理";
        QuickReadLabel.Text = "TL;DR · " + state;
        QuickReadLabel.ToolTip = "核心含义速览，不涵盖全部用法。模型速记与完整释义一同生成，详细含义和参考资料见下方。";
        QuickReadText.Text = string.Join("；", meanings.Select(m => m.Text));
        QuickReadText.ToolTip = string.Join("\n", meanings.Select(m => m.Label + "：" + m.Text));
    }
    private Border ReferenceSenseRow(SpecialistReference brief, int index)
    {
        var content = new StackPanel();
        content.Children.Add(CompactHint($"{index + 1:00}  " + LexicalLabels.Usage(brief.Domain) + " · 资料摘要"));
        if (brief.Expansion.Length > 0) { var expansion = CompactText(brief.Expansion, 17); expansion.Foreground = accentBrush; content.Children.Add(expansion); }
        content.Children.Add(CompactText(brief.Chinese, 20)); content.Children.Add(CompactText(brief.English, 18));
        var controls = new WrapPanel(); var details = new StackPanel(); var source = new StackPanel();
        source.Children.Add(CompactHint("LeXi 根据原始资料整理；模型尚未提供对应释义。"));
        var link = new Button { Content = brief.SourceName + " ↗", FontSize = 14, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Left };
        link.Click += (_, _) => OpenSource(brief.SourceUrl); source.Children.Add(link);
        content.Children.Add(controls); content.Children.Add(details);
        AddDisclosure(controls, details, "reference:" + brief.Word, "参考资料", source);
        return new Border { Child = content, BorderBrush = Theme.Brush("Stroke"), BorderThickness = new(0,index == 0 ? 0 : 1,0,0), Padding = new(0,index == 0 ? 0 : 10,0,8) };
    }
}
