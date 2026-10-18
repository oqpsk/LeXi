using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Lexi;
using Lexi.Core;

internal static partial class Program
{
    private static void CheckParallelResults(MainWindow main, SettingsWindow settings)
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
        var tabs = Contents(settings).OfType<TabControl>().Single();
        tabs.SelectedIndex = 0;
        Measure((FrameworkElement)settings.Content, 538, 478);
        var models = Descendants((DependencyObject)settings.Content).OfType<ComboBox>().Where(p => p.ItemsSource is System.Collections.Generic.IEnumerable<ModelChoice>).ToArray();
        Check(models.Length == 3 && models[0].SelectedItem.ToString()!.Contains("Sol") &&
            models[1].SelectedItem.ToString()!.Contains("Haiku") && models[2].SelectedItem.ToString()!.Contains("Luna"),
            "settings show the preserved primary, complementary secondary and independent judge");

        var primary = new LookupModel("codex", "gpt-6.1-sol"); var secondary = new LookupModel("claude", "haiku");
        var sense = new AiSense("noun", "A confident male archetype.", "自信的男性典型。", "internet slang", "He is a Chad.", "他是一位 Chad。", [1]);
        var alternate = sense with { Chinese = "充满自信的男性形象。", Evidence = [] };
        var country = sense with { English = "A country in Africa.", Chinese = "非洲的一个国家。", Register = "geography" };
        var answers = new[] { new ModelAnswer(primary, ModelPhase.Complete, new("chad", true, [sense, country], "", "source_based")),
            new ModelAnswer(secondary, ModelPhase.Complete, new("chad", true, [alternate], "", "unverified")) };
        var groups = SenseMerging.Group(answers, new("fixture", [new("m0s0", "m1s0", "equivalent", 0.99)]));
        var snapshot = new LookupSnapshot(answers, groups);
        var evidence = new OnlineEntry("chad", [new("noun", "Synthetic source definition", [], "Fixture source", "https://example.org/chad")], "https://example.org/chad", DateTimeOffset.UtcNow);
        var local = (Panel)main.FindName("LocalPanel");
        var original = new TextBlock { Text = "Original dictionary text remains unchanged." };
        local.Children.Add(new Border { Child = original });
        ((FrameworkElement)main.FindName("EntryPanel")).Visibility = Visibility.Visible;
        main.RenderAnswers(snapshot, evidence);
        Measure((FrameworkElement)main.Content, 558, 398);
        var ai = (Panel)main.FindName("AiPanel");
        var visible = Contents(ai, true).OfType<TextBox>().Select(t => t.Text).ToArray();
        Check(visible.Contains(sense.Chinese) && visible.Contains(country.Chinese) && !visible.Contains(alternate.Chinese),
            "merged results show one wording and retain the distinct country meaning");
        var alternatives = Contents(ai).OfType<ToggleButton>().Single(t => t.Content.ToString()!.StartsWith("其他表述"));
        Check(Contents(ai).OfType<TextBox>().Any(t => t.Text == alternate.Chinese) &&
            Contents(ai).OfType<TextBlock>().Any(t => t.Text.Contains("现有词典原文不足以核对全部内容")),
            "collapsed alternative keeps its original text and unverified label");
        alternatives.IsChecked = true;
        Measure((FrameworkElement)main.Content, 558, 398);
        Check(Contents(ai, true).OfType<TextBox>().Any(t => t.Text == alternate.Chinese), "expanded alternate definitions remain selectable");
        var originals = local.Children.OfType<Expander>().Single();
        Check(!originals.IsExpanded && Contents(originals).Contains(original), "offline source text is retained verbatim in a collapsed section");
        originals.IsExpanded = true;
        main.RenderAnswers(snapshot with { Merging = true }, evidence);
        Check(originals.IsExpanded, "new model progress preserves the source section expansion");
        main.RenderAnswers(new(answers.Select(a => a with { Entry = null, Phase = ModelPhase.Failed }).ToArray(), []), evidence);
        Check(originals.IsExpanded, "original dictionary content stays available when model results fail");
        Console.WriteLine($"{count} parallel-result presentation checks passed without displaying windows");
    }
}
