using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Lexi;
using Lexi.Core;

internal static partial class Program
{
    private static void CheckInstantLookup(MainWindow main)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
        var clock = new CopyClock(); var copy = new RecentCopy(clock);
        copy.Observe(1, true); Check(copy.Take() == null, "preexisting clipboard contents are not treated as a fresh copy");
        copy.Observe(2, false); Check(copy.Take() == 2 && copy.Take() == null, "an external copy is consumed once");
        copy.Observe(3, true); Check(copy.Take() == null, "LeXi's own clipboard owner cannot trigger another lookup");
        copy.Observe(4, false); clock.Advance(8001); Check(copy.Take() == null, "a copy older than eight seconds is ignored");
        copy.Observe(5, false); clock.Advance(7999); Check(copy.Take() == 5, "the freshness boundary is measured from the notification");

        var model = new LookupModel("codex", "synthetic");
        var entry = new AiEntry("bank", true, [new("noun", "a financial institution", "银行", "", "A bank.", "一家银行。", [])], "", "unverified");
        var answer = new ModelAnswer(model, ModelPhase.Complete, entry);
        main.RenderAnswers(new([answer], SenseMerging.Group([answer])), null);
        var panel = (StackPanel)main.FindName("AiPanel");
        var rows = Descendants(panel).OfType<Border>().Where(b => b.Focusable && AutomationProperties.GetHelpText(b).Contains("空格朗读")).ToArray();
        Check(rows.Length == 1 && KeyboardNavigation.GetIsTabStop(rows[0]) && AutomationProperties.GetName(rows[0]) == "银行",
            "dictionary senses expose keyboard focus and meaningful accessible instructions");

        var heading = new SenseHeadingView("01", "noun", "", "", false, false, "", true);
        var method = typeof(MainWindow).GetMethod("SenseHeading", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var label = (TextBlock)method.Invoke(main, [heading])!;
        Check(label.Inlines.OfType<InlineUIContainer>().Any(i => i.Child is Border b && AutomationProperties.GetName(b).Contains("本句")),
            "Windows renders Core's in-context sense marker");
        Check(new AppSettings() is { LookupCopiedText: true, LookupSelectedText: false }, "clipboard lookup is enabled and selection reading is opt-in by default");
    }
    private sealed class CopyClock : TimeProvider
    {
        private long now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => now;
        internal void Advance(long ms) => now += ms;
    }
}
