using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    private WordFormSet[] localWordForms = [], onlineWordForms = [];
    private void ResetWordForms() { localWordForms = []; onlineWordForms = []; FormsPanel.Children.Clear(); }
    private void ApplyLocalWordForms(string query, WordFormSet[] entries)
    { localWordForms = entries; RenderWordForms(query, WordForms.Combine(localWordForms, onlineWordForms)); }
    private void ApplyOnlineWordForms(string query, WordFormSet[] entries)
    { onlineWordForms = entries; RenderWordForms(query, WordForms.Combine(localWordForms, onlineWordForms)); }

    internal void RenderWordForms(string query, WordFormSet[] entries)
    {
        FormsPanel.Children.Clear();
        foreach (var entry in entries)
        {
            var forms = new WrapPanel { Margin = new(0,0,0,9) };
            var past = entry.Forms.FirstOrDefault(f => f.Kind == "p");
            var participle = entry.Forms.FirstOrDefault(f => f.Kind == "d");
            var sharedPast = past != null && participle != null && past.Words.SequenceEqual(participle.Words);
            var lemma = Queries.Normalize(query) == Queries.Normalize(entry.Lemma) ? entry.Lemma : query + " → " + entry.Lemma;
            foreach (var original in new[] { new WordForm("0", "原形", [lemma]) }.Concat(entry.Forms))
            {
                if (sharedPast && original.Kind == "d") continue;
                var form = sharedPast && original.Kind == "p" ? original with { Label = "过去式 / 过去分词",
                    SourceName = string.Join(" / ", new[] { original.SourceName, participle!.SourceName }.Where(s => s.Length > 0).Distinct()),
                    SourceUrl = string.Join("\n", new[] { original.SourceUrl, participle!.SourceUrl }.Where(s => s.Length > 0).Distinct()) } : original;
                var source = form.SourceName.Length > 0 ? form.SourceName : entry.SourceName;
                var url = form.SourceUrl.Length > 0 ? form.SourceUrl : entry.SourceUrl;
                var cell = new TextBlock { Margin = new(0,0,16,4), ToolTip = "词形来源：" + source + "\n" + url };
                // Inline runs share a typographic baseline even with different fonts and sizes.
                cell.Inlines.Add(new Run(form.Label + "  ") { FontSize = 13, Foreground = muted });
                var spelling = string.Join(" / ", form.Words);
                cell.Inlines.Add(new Run(spelling) { FontFamily = (FontFamily)FindResource("LexicalFont"), FontSize = 18,
                    Foreground = form.Words.Any(w => Queries.Normalize(w) == Queries.Normalize(query)) ? accentBrush : Foreground });
                var copy = new MenuItem { Header = "复制词形" };
                copy.Click += (_, _) => { try { Clipboard.SetText(string.Join(" / ", form.Kind == "0" ? [entry.Lemma] : form.Words)); }
                    catch (System.Runtime.InteropServices.COMException) { } };
                cell.ContextMenu = new ContextMenu(); cell.ContextMenu.Items.Add(copy);
                forms.Children.Add(cell);
            }
            FormsPanel.Children.Add(forms);
        }
    }
}
