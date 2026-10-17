using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    private WordFormSet[] localWordForms = [], onlineWordForms = [];
    private readonly Dictionary<LookupModel, AiEntry> wordFormAnswers = [];
    private LookupModel[] wordFormModelOrder = [];
    private string wordFormQuery = "";
    internal void ResetWordForms() { localWordForms = []; onlineWordForms = []; wordFormAnswers.Clear(); wordFormModelOrder = []; wordFormQuery = ""; FormsPanel.Children.Clear(); }
    private bool MatchWordFormQuery(string query)
    {
        if (wordFormQuery.Length > 0 && wordFormQuery != query) return false;
        wordFormQuery = query; return true;
    }
    internal void ApplyLocalWordForms(string query, WordFormSet[] entries)
    { if (!MatchWordFormQuery(query)) return; localWordForms = entries; RefreshWordForms(query); }
    internal void ApplyOnlineWordForms(string query, WordFormSet[] entries)
    { if (!MatchWordFormQuery(query)) return; onlineWordForms = entries; RefreshWordForms(query); }
    internal void ApplyModelWordForms(string query, LookupSnapshot snapshot)
    {
        if (query.Length == 0 || !MatchWordFormQuery(query)) return;
        wordFormModelOrder = snapshot.Answers.Select(a => a.Model).ToArray();
        var changed = false;
        foreach (var answer in snapshot.Answers)
        {
            // Keep the last complete data while a replacement streams. Partial morphology
            // never becomes visible or cached, even if a provider preview includes it.
            if (answer.Phase == ModelPhase.Streaming) continue;
            if (answer.Entry is { Found: true, WordForms: not null } entry && Queries.Normalize(entry.Word) == query)
            { wordFormAnswers[answer.Model] = entry; changed = true; }
            else if (answer.Entry is { Found: false } || answer.Phase == ModelPhase.Failed && !answer.Cached)
                changed |= wordFormAnswers.Remove(answer.Model);
        }
        if (changed) RefreshWordForms(query);
    }
    private void RefreshWordForms(string query) => RenderWordForms(query, ModelInflections.Supplement(query,
        WordForms.Combine(localWordForms, onlineWordForms), wordFormModelOrder.Where(wordFormAnswers.ContainsKey).Select(m => (m, wordFormAnswers[m]))));

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
                    SourceUrl = string.Join("\n", new[] { original.SourceUrl, participle!.SourceUrl }.Where(s => s.Length > 0).Distinct()),
                    Note = string.Join("\n", new[] { original.Note, participle!.Note }.Where(s => s.Length > 0).Distinct()) } : original;
                var source = form.SourceName.Length > 0 ? form.SourceName : entry.SourceName;
                var url = form.SourceUrl.Length > 0 ? form.SourceUrl : entry.SourceUrl;
                var cell = new TextBlock { Margin = new(0,0,16,4), ToolTip = string.Join("\n", new[] { "词形来源：" + source, url, form.Note }.Where(s => s.Length > 0)) };
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
