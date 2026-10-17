using System.Windows;
using System.Windows.Controls;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    private void RenderTranslation(TranslationView? view)
    {
        AiPanel.Children.Clear();
        if (view == null) return;
        if (view.Error.Length > 0) { AiPanel.Children.Add(Hint(view.Error)); return; }
        var card = Card(AiPanel, "翻译", view.Source, true);
        card.Children.Add(Hint(view.Hint));
        foreach (var paragraph in view.Paragraphs) { var block = Selectable(paragraph, 18); block.Margin = new(0,4,0,14); card.Children.Add(block); }
        if (!string.IsNullOrWhiteSpace(view.Note)) card.Children.Add(Hint(view.Note));
        if (!view.Complete) return;
        var copy = new Button { Content = "复制译文", Foreground = accentBrush, HorizontalAlignment = HorizontalAlignment.Left, Padding = new(0,8,0,2) };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(string.Join("\n\n", view.Paragraphs)); }
            catch (System.Runtime.InteropServices.COMException) { LookupStatus.Text = "剪贴板暂时被占用，请重试。"; }
        };
        card.Children.Add(copy);
    }
}
