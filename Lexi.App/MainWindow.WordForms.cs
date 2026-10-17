using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    internal void ResetWordForms() => session.ResetWordForms();
    internal void ApplyLocalWordForms(string query, WordFormSet[] entries) => session.ApplyLocalWordForms(query, entries);
    internal void ApplyOnlineWordForms(string query, WordFormSet[] entries) => session.ApplyOnlineWordForms(query, entries);
    internal void ApplyModelWordForms(string query, LookupSnapshot snapshot) => session.ApplyModelWordForms(query, snapshot);
    internal void RenderWordForms(string query, WordFormSet[] entries) => RenderFormRows(QueryPresentation.Rows(query, entries));

    private void RenderFormRows(WordFormRow[] rows)
    {
        FormsPanel.Children.Clear();
        foreach (var row in rows)
        {
            var forms = new WrapPanel { Margin = new(0,0,0,9) };
            foreach (var form in row.Cells)
            {
                var cell = new TextBlock { Margin = new(0,0,16,4), ToolTip = form.Tooltip };
                // Inline runs share a typographic baseline even with different fonts and sizes.
                cell.Inlines.Add(new Run(form.Label + "  ") { FontSize = 13, Foreground = muted });
                cell.Inlines.Add(new Run(form.Spelling) { FontFamily = (FontFamily)FindResource("LexicalFont"), FontSize = 18,
                    Foreground = form.Matches ? accentBrush : Foreground });
                var copy = new MenuItem { Header = "复制词形" };
                copy.Click += (_, _) => { try { Clipboard.SetText(form.CopyText); }
                    catch (System.Runtime.InteropServices.COMException) { } };
                cell.ContextMenu = new ContextMenu(); cell.ContextMenu.Items.Add(copy);
                forms.Children.Add(cell);
            }
            FormsPanel.Children.Add(forms);
        }
    }
}
