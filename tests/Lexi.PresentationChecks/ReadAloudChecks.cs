using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Lexi;
using Lexi.Core;

internal static partial class Program
{
    private static void CheckReadAloud(MainWindow main)
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
        var model = new LookupModel("codex", "gpt-6.1-sol");
        var sense = new AiSense("noun", "The property of being unchanged by repetition.", "幂等性。", "computing",
            "Retries are safe because the request is idempotent.", "请求是幂等的，所以可以安全重试。", [1]);
        var answers = new[] { new ModelAnswer(model, ModelPhase.Complete, new("idempotency", true, [sense], "", "source_based")) };
        ((FrameworkElement)main.FindName("EntryPanel")).Visibility = Visibility.Visible;
        main.RenderAnswers(new LookupSnapshot(answers, SenseMerging.Group(answers, new("fixture", []))), null);
        Measure((FrameworkElement)main.Content, 718, 618);
        var ai = (Panel)main.FindName("AiPanel");
        TextBox Text(string value) => Contents(ai).OfType<TextBox>().First(t => t.Text == value);
        Grid Block(TextBox text) => (Grid)((Border)text.Parent).Parent;
        Button Speaker(TextBox text) => Block(text).Children.OfType<Button>().Single();

        var definition = Text(sense.Chinese);
        var menu = definition.ContextMenu!;
        Check(menu.Items[0] is MenuItem { Header: "朗读" } && menu.Items.OfType<MenuItem>().Any(i => i.Command == ApplicationCommands.Copy),
            "definitions offer 朗读 above copy in their context menu");
        var speaker = Speaker(definition);
        Check(speaker.Visibility == Visibility.Hidden && AutomationProperties.GetName(speaker) == "朗读", "the read-aloud speaker stays out of sight at rest");
        Block(definition).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        Check(speaker.Visibility == Visibility.Visible, "pointing at a definition shows its speaker in the margin");
        Block(definition).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
        Check(speaker.Visibility == Visibility.Hidden, "the speaker hides again when the pointer leaves");
        Check(Text(sense.English).ContextMenu?.Items[0] is MenuItem { Header: "朗读" }, "English definitions can be read aloud too");
        var example = Text(sense.Example);
        Check(example.ContextMenu?.Items[0] is MenuItem { Header: "朗读" } && Text(sense.ExampleChinese).ContextMenu?.Items[0] is MenuItem { Header: "朗读" },
            "examples and their translations can be read aloud");
        Check(Math.Abs(Speaker(example).Margin.Left - (speaker.Margin.Left - 12)) < 0.01,
            "an example's speaker moves out by the disclosure indent, sharing the definitions' column");
        Console.WriteLine($"{count} read-aloud presentation checks passed without displaying windows");
    }
}
