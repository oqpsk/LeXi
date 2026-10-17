using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Lexi;
using Lexi.Core;

internal static partial class Program
{
    private static void CheckDictionaryUsability(MainWindow main, SettingsWindow settings)
    {
        int count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); count++; }
        var online = (ToggleButton)main.FindName("OnlineToggle");
        var oldLabel = online.Content;
        double Width(string label) { online.Content = label; Measure((FrameworkElement)main.Content, 558, 398); return online.ActualWidth; }
        Check(Width("在线补充") == Width("离线查询"), "online mode text cannot shift the switch or adjacent regeneration button");
        online.Content = oldLabel;
        var model = new LookupModel("codex", "test");
        var answers = new[] { new ModelAnswer(model, ModelPhase.Complete, new("tucking", true,
            [new("verb (gerund/present participle)", "folding cloth", "折叠布料", "standard", "", "", []),
             new("noun", "a fold of cloth", "布料上的褶皱", "technical, computing", "", "", [])], "", "unverified")) };
        main.RenderAnswers(new(answers, SenseMerging.Group(answers)), null);
        var ai = (Panel)main.FindName("AiPanel");
        var runs = Contents(ai, true).OfType<TextBlock>().SelectMany(t => t.Inlines.OfType<Run>()).ToArray();
        var wordClasses = runs.Where(r => r.Text is "动词" or "名词").ToArray();
        Check(wordClasses.Length == 2 && wordClasses.All(r => r.FontSize >= 16 && r.FontWeight == FontWeights.SemiBold &&
            r.Foreground is SolidColorBrush b && b.Color == ((SolidColorBrush)Theme.Brush("Warning")).Color),
            "noun and verb senses have prominent warm-colored word-class labels");
        Check(runs.All(r => !r.Text.Contains("未核验") && !r.Text.Contains("standard")) &&
            Contents(ai, true).OfType<ToggleButton>().Count(t => t.Content.ToString() == "来源 · 模型补充") == 2,
            "plain source disclosures retain model-origin context without alarm-colored evidence verdicts");
        main.RenderWordForms("went", [new("go", [new("p","过去式",["went"]),new("d","过去分词",["gone"]),new("i","现在分词",["going"]),new("3","第三人称单数",["goes"])], "Synthetic source","https://example.org/forms")]);
        main.RenderAnswers(new(answers, SenseMerging.Group(answers)), null);
        var forms = (Panel)main.FindName("FormsPanel");
        Check(Contents(forms, true).OfType<TextBox>().Any(t => t.Text == "gone") &&
            Contents(forms, true).OfType<TextBlock>().Any(t => t.Text.Contains("went → go")),
            "recorded inflections remain visible when model results replace the definition card");
        main.RenderWordForms("unknown", []);
        Check(forms.Children.Count == 0, "missing forms clear the previous word's inflections");
        var tabs = Contents(settings).OfType<TabControl>().Single();
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => (string)t.Header == "账号连接");
        Measure((FrameworkElement)settings.Content, 538, 478);
        Check(Descendants((DependencyObject)settings.Content).OfType<Button>().Count(b => AutomationProperties.GetName(b).StartsWith("登录 ")) == 2 &&
            !Contents(settings).OfType<TextBlock>().Any(t => t.Text.Contains("someone@example.org")),
            "connection page offers separate accessible official sign-in controls without account identifiers");
        Check(main.Title == "LeXi" && settings.Title == "LeXi 设置", "product windows use the LeXi brand");
        Console.WriteLine($"{count} dictionary usability presentation checks passed without displaying windows");
    }
}
