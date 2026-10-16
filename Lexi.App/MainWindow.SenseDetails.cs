using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    private readonly Dictionary<string, bool> senseDetails = new(StringComparer.Ordinal);
    internal void ResetSenseDetails() => senseDetails.Clear();

    // Content identity survives streaming, reordered senses and the final merge.
    // Keep expansion only for this lookup, never in the user's disk cache.
    private static string SenseKey(SenseCandidate candidate) => Cache.Hash(JsonSerializer.Serialize(new
    { candidate.Model, candidate.Sense.Chinese, candidate.Sense.English }, Json.Options));

    private TextBlock CompactHint(string text, bool warning = false) => new()
    { Text = text, Foreground = warning ? Theme.Brush("Warning") : muted, FontSize = 14, Margin = new(0,2,0,6), TextWrapping = TextWrapping.Wrap };

    private TextBox CompactText(string text, double size)
    {
        var block = Selectable(text, size);
        block.Margin = new(0,2,0,3);
        return block;
    }

    private static string Provenance(SenseCandidate candidate) => candidate.Provisional ? "生成中 · 来源待确认" :
        candidate.EvidenceStatus == "source_based" ? "据词典来源整理 · 含 AI 生成内容" : "AI 补充 · 未经词典来源核验";

    private TextBlock SenseHeading(SenseCandidate candidate, string prefix, int variants = 1)
    {
        var label = CompactHint(""); label.Margin = new(0,0,0,3);
        label.ToolTip = candidate.Model.Name + "\n" + Provenance(candidate);
        label.Inlines.Add(new Run(prefix + "  ") { Foreground = green });
        label.Inlines.Add(new Run(candidate.Sense.PartOfSpeech +
            (string.IsNullOrWhiteSpace(candidate.Sense.Register) ? "" : " · " + candidate.Sense.Register)));
        var shortStatus = candidate.Provisional ? "生成中 · 待核验" : candidate.EvidenceStatus == "source_based" ? "AI · 据来源" : "AI · 未核验";
        label.Inlines.Add(new Run(" · " + shortStatus) { Foreground = !candidate.Provisional && candidate.EvidenceStatus != "source_based" ? Theme.Brush("Warning") : muted });
        if (variants > 1) label.Inlines.Add(new Run($" · {variants} 份表述"));
        return label;
    }

    private Border SenseRow(SenseGroup group, int index, OnlineEntry? evidence)
    {
        var content = new StackPanel();
        content.Children.Add(SenseHeading(group.Representative, $"{index + 1:00}", group.Variants.Length));
        RenderSenseContent(content, group.Representative, evidence, group.Variants.Where(v => v.Id != group.Representative.Id).ToArray());
        return new Border { Child = content, BorderBrush = Theme.Brush("Stroke"), BorderThickness = new(0,index == 0 ? 0 : 1,0,0), Padding = new(0,index == 0 ? 0 : 10,0,8) };
    }

    private void RenderSenseContent(Panel parent, SenseCandidate candidate, OnlineEntry? evidence, SenseCandidate[]? alternatives = null)
    {
        var sense = candidate.Sense;
        var key = SenseKey(candidate);
        parent.Children.Add(CompactText(sense.Chinese, 20));
        parent.Children.Add(CompactText(sense.English, 18));
        var controls = new WrapPanel { Margin = new(0,2,0,0) }; var details = new StackPanel();
        parent.Children.Add(controls); parent.Children.Add(details);
        if (!string.IsNullOrWhiteSpace(sense.Example) || !string.IsNullOrWhiteSpace(sense.ExampleChinese))
        {
            var examples = new StackPanel();
            if (!string.IsNullOrWhiteSpace(sense.Example)) examples.Children.Add(CompactText(sense.Example, 16));
            if (!string.IsNullOrWhiteSpace(sense.ExampleChinese)) examples.Children.Add(CompactText(sense.ExampleChinese, 16));
            AddDisclosure(controls, details, key + ":examples", "例句", examples);
        }

        var sources = new StackPanel();
        sources.Children.Add(CompactHint(candidate.Model.Name + " · " + Provenance(candidate)));
        var numbers = candidate.Provisional || evidence == null ? [] : sense.Evidence.Distinct().Where(n => n >= 1 && n <= evidence.Senses.Length).ToArray();
        var links = new WrapPanel(); sources.Children.Add(links);
        foreach (var number in numbers)
        {
            var cited = evidence!.Senses[number - 1];
            var link = new Button { Content = $"[{number}] {cited.SourceName} ↗", FontSize = 14, Foreground = muted, Padding = new(0,3,12,3), ToolTip = cited.Definition };
            link.Click += (_, _) => OpenSource(string.IsNullOrEmpty(cited.SourceUrl) ? evidence.Url : cited.SourceUrl);
            links.Children.Add(link);
        }
        if (numbers.Length == 0 && !candidate.Provisional) sources.Children.Add(CompactHint("未引用词典来源。"));
        AddDisclosure(controls, details, key + ":sources", numbers.Length == 0 ? "来源" : $"来源 · {numbers.Length}", sources);

        if (alternatives is not { Length: > 0 }) return;
        var originals = new StackPanel();
        foreach (var variant in alternatives)
        {
            var section = new StackPanel { Margin = new(0,2,0,6) };
            section.Children.Add(SenseHeading(variant, variant.Model.Name));
            RenderSenseContent(section, variant, evidence);
            originals.Children.Add(section);
        }
        // If the user was reading the faster model before merging, keep that
        // expanded detail accessible when it moves under the primary wording.
        var retainReading = alternatives.Any(v => senseDetails.Any(s => s.Value && s.Key.StartsWith(SenseKey(v) + ":", StringComparison.Ordinal)));
        AddDisclosure(controls, details, key + ":alternatives", $"其他表述 · {alternatives.Length}", originals, retainReading);
    }

    private void AddDisclosure(Panel controls, Panel details, string key, string label, FrameworkElement content, bool initiallyOpen = false)
    {
        var open = senseDetails.GetValueOrDefault(key, initiallyOpen);
        var body = new Border { Child = content, BorderBrush = Theme.Brush("Stroke"), BorderThickness = new(2,0,0,0),
            Padding = new(10,4,0,2), Margin = new(0,5,0,3), Visibility = open ? Visibility.Visible : Visibility.Collapsed };
        var toggle = new ToggleButton { Content = label, Tag = key, IsChecked = open, Style = (Style)FindResource("DisclosureButton"), ToolTip = "展开或收起" + label };
        AutomationProperties.SetName(toggle, label);
        toggle.Checked += (_, _) => { senseDetails[key] = true; body.Visibility = Visibility.Visible; };
        toggle.Unchecked += (_, _) => { senseDetails[key] = false; body.Visibility = Visibility.Collapsed; };
        controls.Children.Add(toggle); details.Children.Add(body);
    }
}
