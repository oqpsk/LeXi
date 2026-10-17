using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
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

    private TextBox ExampleText(string text, bool chinese = false)
    {
        var block = CompactText(text, 18);
        block.FontFamily = (FontFamily)FindResource(chinese ? "ChineseExampleFont" : "LexicalFont");
        block.FontStyle = FontStyles.Italic;
        return block;
    }

    private static string Provenance(SenseCandidate candidate) => candidate.Provisional ? "释义仍在生成，来源信息尚未完整。" :
        candidate.EvidenceStatus == "source_based" ? "模型参考下列资料整理；释义与例句由模型生成。" :
        "这条解释由模型补充，现有词典原文不足以核对全部内容；这不表示释义错误。";

    private TextBlock SenseHeading(SenseCandidate candidate, string prefix)
    {
        var label = CompactHint(""); label.Margin = new(0,0,0,3);
        label.ToolTip = candidate.Sense.PartOfSpeech + " · " + candidate.Sense.Register;
        label.Inlines.Add(new Run(prefix + "  ") { Foreground = accentBrush });
        label.Inlines.Add(new Run(LexicalLabels.PartOfSpeech(candidate.Sense.PartOfSpeech))
            { Foreground = Theme.Brush("Warning"), FontFamily = (FontFamily)FindResource("LexicalFont"), FontStyle = FontStyles.Italic, FontSize = 18 });
        var form = LexicalLabels.Form(candidate.Sense.PartOfSpeech);
        if (form.Length > 0) label.Inlines.Add(new Run("  " + form) { FontFamily = (FontFamily)FindResource("LexicalFont"), FontStyle = FontStyles.Italic, FontSize = 16 });
        var usage = LexicalLabels.Usage(candidate.Sense.Register);
        if (usage.Length > 0) label.Inlines.Add(new Run("  " + usage));
        if (LexicalCoverage.Abbreviation(candidate.Sense) && !usage.Contains("缩写")) label.Inlines.Add(new Run("  缩写"));
        if (candidate.Provisional) label.Inlines.Add(new Run(" · 生成中"));
        return label;
    }

    private Border SenseRow(SenseGroup group, int index, OnlineEntry? evidence)
    {
        var content = new StackPanel();
        content.Children.Add(SenseHeading(group.Representative, $"{index + 1:00}"));
        RenderSenseContent(content, group.Representative, evidence, group.Variants.Where(v => v.Id != group.Representative.Id).ToArray(), group.IncludesUsageVariants);
        return new Border { Child = content, BorderBrush = Theme.Brush("Stroke"), BorderThickness = new(0,index == 0 ? 0 : 1,0,0), Padding = new(0,index == 0 ? 0 : 10,0,8) };
    }

    private void RenderSenseContent(Panel parent, SenseCandidate candidate, OnlineEntry? evidence, SenseCandidate[]? alternatives = null, bool usageVariants = false)
    {
        var sense = candidate.Sense;
        var key = SenseKey(candidate);
        if (LexicalCoverage.Abbreviation(sense) && sense.Expansion.Length > 0)
        {
            var fullForm = CompactText(sense.Expansion, 17);
            fullForm.FontFamily = (FontFamily)FindResource("LexicalFont");
            fullForm.Foreground = accentBrush; fullForm.ToolTip = "英文全称";
            parent.Children.Add(fullForm);
        }
        parent.Children.Add(CompactText(sense.Chinese, 20));
        parent.Children.Add(CompactText(sense.English, 18));
        var controls = new WrapPanel { Margin = new(0,2,0,0) }; var details = new StackPanel();
        parent.Children.Add(controls); parent.Children.Add(details);
        if (!string.IsNullOrWhiteSpace(sense.Example) || !string.IsNullOrWhiteSpace(sense.ExampleChinese))
        {
            var examples = new StackPanel();
            if (!string.IsNullOrWhiteSpace(sense.Example)) examples.Children.Add(ExampleText(sense.Example));
            if (!string.IsNullOrWhiteSpace(sense.ExampleChinese)) examples.Children.Add(ExampleText(sense.ExampleChinese, true));
            AddDisclosure(controls, details, key + ":examples", "例句", examples);
        }

        var sources = new StackPanel();
        sources.Children.Add(CompactHint(candidate.Model.Name + " · " + Provenance(candidate)));
        sources.Children.Add(CompactHint("原始用法标签：" + candidate.Sense.PartOfSpeech + " · " + candidate.Sense.Register));
        var numbers = candidate.Provisional || evidence == null ? [] : new[] { candidate }.Concat(alternatives ?? [])
            .Where(v => !v.Provisional).SelectMany(v => v.Sense.Evidence).Distinct().Where(n => n >= 1 && n <= evidence.Senses.Length).ToArray();
        var links = new WrapPanel(); sources.Children.Add(links);
        foreach (var number in numbers)
        {
            var cited = evidence!.Senses[number - 1];
            var link = new Button { Content = $"[{number}] {cited.SourceName} ↗", FontSize = 14, Foreground = muted, Padding = new(0,3,12,3), ToolTip = cited.Definition };
            link.Click += (_, _) => OpenSource(string.IsNullOrEmpty(cited.SourceUrl) ? evidence.Url : cited.SourceUrl);
            links.Children.Add(link);
            foreach (var brief in SpecialistReferences.Cited(cited))
            {
                sources.Children.Add(CompactHint(brief.SourceName + " · LeXi 参考摘要"));
                sources.Children.Add(CompactText(brief.Chinese, 16));
                sources.Children.Add(CompactText(brief.English, 16));
            }
        }
        if (numbers.Length == 0 && !candidate.Provisional) sources.Children.Add(CompactHint("当前解释来自模型知识，未附可直接核对的资料。"));
        if (alternatives?.Length > 0) sources.Children.Add(CompactHint("资料链接包含同义项的其他表述所引用的内容。"));
        var sourceLabel = numbers.Length > 0 ? $"参考资料 · {numbers.Length}" : "参考资料";
        var keepReferenceOpen = numbers.Any(n => SpecialistReferences.Cited(evidence!.Senses[n - 1])
            .Any(brief => senseDetails.GetValueOrDefault("reference:" + brief.Word)));
        AddDisclosure(controls, details, key + ":sources", sourceLabel, sources, keepReferenceOpen);

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
        AddDisclosure(controls, details, key + ":alternatives", $"{(usageVariants ? "用法细分" : "其他表述")} · {alternatives.Length}", originals, retainReading);
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
