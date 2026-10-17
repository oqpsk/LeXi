using System.Windows;
using System.Windows.Controls;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    internal void RenderWordForms(string query, WordFormSet[] entries)
    {
        FormsPanel.Children.Clear();
        foreach (var entry in entries)
        {
            var section = new StackPanel { Margin = new(0,0,0,12) };
            var roles = entry.Roles(query);
            var heading = Queries.Normalize(query) != Queries.Normalize(entry.Lemma)
                ? $"{query} → {entry.Lemma}" + (roles.Length > 0 ? " · " + string.Join(" / ", roles) : "") : "词形变化";
            section.Children.Add(new TextBlock { Text = heading, FontSize = 15, Foreground = muted, Margin = new(0,0,0,5), ToolTip = "词形来源：" + entry.SourceName });
            var forms = new WrapPanel(); section.Children.Add(forms);
            foreach (var form in new[] { new WordForm("0", "原形", [entry.Lemma]) }.Concat(entry.Forms))
            {
                var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0,0,14,4) };
                cell.Children.Add(new TextBlock { Text = form.Label, FontSize = 13, Foreground = muted, VerticalAlignment = VerticalAlignment.Center, Margin = new(0,0,5,0) });
                var words = CompactText(string.Join(" / ", form.Words), 16); words.Margin = new(0); cell.Children.Add(words);
                forms.Children.Add(cell);
            }
            FormsPanel.Children.Add(section);
        }
    }
}
