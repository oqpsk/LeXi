using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Lexi;
using Lexi.Core;

internal static partial class Program
{
    private static void CheckModelMorphology(MainWindow main)
    {
        int count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); count++; }
        var panel = (Panel)main.FindName("FormsPanel");
        string[] Spellings() => Contents(panel, true).OfType<TextBlock>().Select(t => t.Inlines.OfType<Run>().Last().Text).ToArray();
        var primary = new LookupModel("codex", "test"); var secondary = new LookupModel("claude", "test");
        var verb = new AiEntry("glim", true, [new("verb", "a synthetic action", "测试动作", "", "", "", [])], "", "unverified", WordForms:
            [new("glim", "verb", [new("past", ["glimmed"]), new("pastParticiple", ["glimmed"]), new("presentParticiple", ["glimming"]), new("thirdPerson", ["glims"])])]);
        ModelAnswer[] one = [new(primary, ModelPhase.Complete, verb), new(secondary, ModelPhase.Pending)];
        var snapshot = new LookupSnapshot(one, SenseMerging.Group(one));
        main.ResetWordForms();
        main.ApplyLocalWordForms("glim", [new("glim", [new("s", "复数", ["glims"])], "Local source", "https://example.org/local")]);
        main.ApplyModelWordForms("glim", new([one[0] with { Phase = ModelPhase.Streaming }, one[1]], []));
        Check(!Spellings().Contains("glimming"), "a streaming morphology preview cannot masquerade as a complete word form");
        main.RenderAnswers(snapshot, null, false);
        Check(Spellings().Contains("glimming") && Spellings().Count(s => s == "glims") == 2,
            "a completed model's new verb usage updates word forms beside the existing noun plural");
        main.ApplyModelWordForms("glim", new([one[0] with { Phase = ModelPhase.Streaming, Entry = verb with { WordForms = null } }, one[1]], []));
        Check(Spellings().Contains("glimming"), "complete morphology stays readable while a replacement definition streams");
        var adjective = verb with { Senses = [verb.Senses[0] with { PartOfSpeech = "adjective" }], WordForms =
            [new("glim", "adjective", [new("comparative", ["glimmer"]), new("superlative", ["glimmest"])])] };
        var both = new[] { one[0], new ModelAnswer(secondary, ModelPhase.Complete, adjective) };
        main.RenderAnswers(new(both, SenseMerging.Group(both)), null);
        Check(Spellings().Contains("glimming") && Spellings().Contains("glimmest"), "a later second model can add another part of speech without removing earlier morphology");
        main.ApplyOnlineWordForms("glim", [new("glim", [new("p", "过去式", ["glam"])], "Online source", "https://example.org/online")]);
        Check(Spellings().Contains("glam") && Spellings().Contains("glimming") && Contents(panel, true).OfType<TextBlock>().Any(t =>
            t.ToolTip is string hint && hint.Contains("Online source") && hint.Contains("glimmed")),
            "late dictionary metadata takes priority while model enrichment and alternate proposals remain accessible");
        main.ApplyModelWordForms("glim", new([new(primary, ModelPhase.Failed), new(secondary, ModelPhase.Failed)], []));
        Check(!Spellings().Contains("glimming") && Spellings().Contains("glam") && Spellings().Contains("glims"),
            "model failure keeps sourced forms and does not preserve rejected model proposals");
        main.ResetWordForms(); main.ApplyLocalWordForms("next", []);
        main.ApplyModelWordForms("glim", snapshot); main.ApplyOnlineWordForms("glim", [new("glim", [new("p", "过去式", ["glam"])], "Old source", "")]);
        Check(panel.Children.Count == 0, "stale model and source updates cannot repopulate the next query's forms");
        main.ResetWordForms();
        Console.WriteLine($"{count} model morphology presentation checks passed without displaying windows");
    }
}
