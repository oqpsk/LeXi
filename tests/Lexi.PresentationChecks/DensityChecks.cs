using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using Lexi;
using Lexi.Core;

internal static partial class Program
{
    private static void CheckDensity(MainWindow main)
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
        var sense = new AiSense("noun", "A confident and admired person, especially someone whose actions impress other people.",
            "自信且令人钦佩的人，尤指以行动给别人留下深刻印象的人。", "网络俚语，可褒可贬",
            "They helped everyone finish the project without asking for anything in return.", "他帮助大家完成了项目，没有要求任何回报。", [1, 2, 3]);
        var primary = new LookupModel("codex", "gpt-6.1-sol"); var secondary = new LookupModel("claude", "haiku");
        var senses = Enumerable.Range(1, 5).Select(i => sense with { Chinese = i + ". " + sense.Chinese, English = i + ". " + sense.English }).ToArray();
        var answers = new[] { new ModelAnswer(primary, ModelPhase.Complete, new("fixture", true, senses, "", "unverified")),
            new ModelAnswer(secondary, ModelPhase.Complete, new("fixture", true, senses, "", "unverified")) };
        var evidence = new OnlineEntry("fixture", Enumerable.Range(1, 3).Select(i =>
            new Sense("noun", "Synthetic source definition " + i, [], "Source " + i, "https://example.org/source/" + i)).ToArray(), "https://example.org/fixture", DateTimeOffset.UtcNow);
        var snapshot = new LookupSnapshot(answers, SenseMerging.Group(answers));
        ((FrameworkElement)main.FindName("Welcome")).Visibility = Visibility.Collapsed;
        ((FrameworkElement)main.FindName("EntryPanel")).Visibility = Visibility.Visible;
        main.ResetSenseDetails();
        main.RenderAnswers(snapshot, evidence);
        var ai = (Panel)main.FindName("AiPanel");
        // The same five-sense fixture measured 2510 / 2230 / 2090 DIP in 0.5.0.
        // Allow font differences on CI while requiring a meaningful reduction.
        foreach (var (width, previousHeight) in new[] { (558, 2510), (718, 2230), (1078, 2090) })
        {
            Measure((FrameworkElement)main.Content, width, 618);
            Console.WriteLine($"DENSITY: {width + 2} DIP window; five bilingual senses; {ai.DesiredSize.Height:0} DIP result height");
            Check(ai.DesiredSize.Height < previousHeight * 0.65, $"compact definitions reduce five-sense layout height at {width + 2} DIP");
        }
        TextBox[] Texts() => Contents(ai, true).OfType<TextBox>().ToArray();
        ToggleButton Toggle(string suffix) => Contents(ai, true).OfType<ToggleButton>().First(t => t.Tag is string key && key.EndsWith(suffix));
        Check(Texts().Count(t => t.FontSize == 20) == 5 && Texts().Count(t => t.FontSize == 18) == 5 &&
            Texts().All(t => t.IsReadOnly && t.Text != sense.Example && t.Text != sense.ExampleChinese),
            "all bilingual definitions retain 20/18 DIP selectable text with examples hidden");
        Check(Contents(ai, true).OfType<TextBlock>().Count(t => new TextRange(t.ContentStart, t.ContentEnd).Text.Contains("未核验")) == 5 &&
            !Contents(ai, true).OfType<Button>().Any(t => t.Content.ToString()!.Contains("Source")),
            "uncertainty remains visible while detailed citations are collapsed");
        var exampleToggle = Toggle(":examples");
        Check(exampleToggle.Focusable && exampleToggle.IsTabStop && !Theme.CanDragFrom(exampleToggle), "disclosure controls support keyboard focus and never drag the window");
        exampleToggle.IsChecked = true;
        Measure((FrameworkElement)main.Content, 718, 618);
        Check(Texts().Count(t => t.Text == sense.Example || t.Text == sense.ExampleChinese) == 2,
            "opening an example reveals both selectable languages for only that sense");
        main.RenderAnswers(snapshot with { Merging = true }, evidence);
        Check(Toggle(":examples").IsChecked == true && Texts().Any(t => t.Text == sense.Example), "example expansion survives an incoming model update");
        Toggle(":sources").IsChecked = true;
        Check(Contents(ai, true).OfType<Button>().Count(b => b.ToolTip is string text && text.StartsWith("Synthetic source definition")) == 3,
            "expanded sources retain every numbered citation and original excerpt");
        Toggle(":examples").IsChecked = false;
        main.RenderAnswers(snapshot, evidence);
        Check(Toggle(":examples").IsChecked == false && Toggle(":sources").IsChecked == true,
            "each disclosure retains independent open and closed states");

        main.ResetSenseDetails();
        var otherSense = sense with { Chinese = "另一种中文表述。", English = "Another wording of the same meaning." };
        var otherEntry = new AiEntry("fixture", true, [otherSense], "", "unverified");
        var awaiting = new[] { new ModelAnswer(primary, ModelPhase.Pending), new ModelAnswer(secondary, ModelPhase.Complete, otherEntry) };
        main.RenderAnswers(new(awaiting, SenseMerging.Group(awaiting)), evidence);
        Toggle(":examples").IsChecked = true;
        var finished = new[] { new ModelAnswer(primary, ModelPhase.Complete, new("fixture", true, [sense], "", "unverified")), awaiting[1] };
        var merged = new LookupSnapshot(finished, SenseMerging.Group(finished, new("fixture", [new("m0s0", "m1s0", "equivalent", 0.99)])));
        main.RenderAnswers(merged, evidence);
        Check(Toggle(":alternatives").IsChecked == true && Texts().Any(t => t.Text == otherSense.Chinese) && Texts().Any(t => t.Text == sense.Example),
            "a fast model's open example stays readable when its sense becomes an alternate");
        Toggle(":alternatives").IsChecked = false;
        main.RenderAnswers(merged, evidence);
        Check(Toggle(":alternatives").IsChecked == false, "explicitly closing alternate wording is respected on later updates");
        main.ResetSenseDetails();
        main.RenderAnswers(merged, evidence);
        Check(Contents(ai, true).OfType<ToggleButton>().All(t => t.IsChecked == false), "a new lookup starts with secondary information collapsed");
        Console.WriteLine($"{count} compact-result checks passed without displaying windows");
    }
}
