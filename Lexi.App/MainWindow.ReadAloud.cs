using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Lexi;

// Definitions and examples can be read aloud, the same way as on macOS. A speaker appears in the
// card's left margin while the pointer is over a block and stays as a waveform while it plays (clicking still stops);
// the block is tinted gold while it plays. Its context menu starts with 朗读 (朗读所选文字 for a
// selection, 停止朗读 while it plays).
public partial class MainWindow
{
    internal const string HeadingSpeech = "heading";
    // From a block's text back to the speaker column, at the top level of the answer card.
    internal const double ReadAloudGutter = 28;
    private static readonly Brush ReadingTint = Freeze(new SolidColorBrush(Color.FromArgb(0x29, 0xDD, 0xBD, 0x82)));
    private readonly List<ReadAloudBlock> readAloudBlocks = [];
    private string? speakingKey;

    private static Brush Freeze(Brush brush) { brush.Freeze(); return brush; }

    // Paint relative to visible glyphs while leaving the TextBox's layout and hit targets intact.
    // WPF line boxes have asymmetric leading, particularly with CJK fallback and serif italics.
    private sealed class ReadingSurface : Border
    {
        private double center = double.NaN;
        private bool reading;
        internal void Reading(bool value) { if (reading == value) return; reading = value; InvalidateVisual(); }
        internal void CenterOn(double value) { if (center == value) return; center = value; InvalidateVisual(); }
        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            if (!reading) return;
            var height = RenderSize.Height;
            var top = double.IsFinite(center) ? center - height / 2 : 0;
            drawing.DrawRoundedRectangle(ReadingTint, null, new Rect(0, top, RenderSize.Width, height), 6, 6);
        }
    }

    private sealed class ReadAloudBlock(string key, ReadingSurface surface, Button speaker)
    {
        public string Key { get; } = key;
        public bool Hovering { get; set; }
        public void Show(bool playing, bool audioPlaying = false)
        {
            surface.Reading(playing);
            if (playing)
            {
                if (speaker.Content is not SpeechWaveform) speaker.Content = new SpeechWaveform("Warning", 14);
                ((SpeechWaveform)speaker.Content).IsAnimating = audioPlaying;
            }
            else if (speaker.Content is not System.Windows.Shapes.Path)
            {
                var icon = Theme.Icon("SpeakerIcon"); icon.Width = icon.Height = 14; speaker.Content = icon;
            }
            speaker.Visibility = Hovering || playing ? Visibility.Visible : Visibility.Hidden;
            speaker.ToolTip = playing ? "停止朗读" : "朗读";
            AutomationProperties.SetName(speaker, playing ? "停止朗读" : "朗读");
        }
    }

    // indent: how far a disclosure body indents the block, so every speaker in the card shares one column.
    private FrameworkElement Speakable(TextBox text, string key, double indent = 0)
    {
        var margin = text.Margin; text.Margin = new(0);
        // The tint reaches a little past the text without moving it.
        var surface = new ReadingSurface { Child = text, CornerRadius = new(6), Margin = new(-6,-2,-6,-2), Padding = new(6,2,6,2) };
        var speakerOffset = new TranslateTransform();
        var speaker = new Button { Style = (Style)FindResource("IconButton"), Width = 22, Height = 22,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = speakerOffset };
        var block = new ReadAloudBlock(key, surface, speaker);
        var gutter = ReadAloudGutter + indent;
        // The whole path from the text to its button belongs to this row, including
        // disclosure indentation. A child outside the old row left a hover dead zone.
        var container = new Grid { Background = Brushes.Transparent,
            Margin = new(margin.Left - gutter, margin.Top, margin.Right, margin.Bottom), Tag = key };
        container.ColumnDefinitions.Add(new() { Width = new(gutter) });
        container.ColumnDefinitions.Add(new());
        Grid.SetColumn(surface, 1);
        container.Children.Add(surface); container.Children.Add(speaker);
        AlignReadAloud(text, surface, container, speakerOffset, speaker.Height);
        container.MouseEnter += (_, _) => { block.Hovering = true; block.Show(speakingKey == key, audioPlaying); };
        container.MouseLeave += (_, _) => { block.Hovering = false; block.Show(speakingKey == key, audioPlaying); };
        speaker.Click += async (_, _) => await ReadAloudAsync(key, text.Text, "");
        text.ContextMenu = ReadAloudMenu(text, key);
        readAloudBlocks.Add(block);
        block.Show(speakingKey == key, audioPlaying);
        return container;
    }

    private static void AlignReadAloud(TextBox text, ReadingSurface surface, Grid row, TranslateTransform offset, double buttonHeight)
    {
        string? previousText = null;
        var previousSize = Size.Empty;
        var previousCaret = Rect.Empty;
        double previousScale = 0;
        text.LayoutUpdated += (_, _) =>
        {
            if (!text.IsArrangeValid || text.LineCount < 1) return;
            var firstCaret = text.GetRectFromCharacterIndex(0);
            if (firstCaret.IsEmpty) return;
            var scale = VisualTreeHelper.GetDpi(text).PixelsPerDip;
            if (text.Text == previousText && text.RenderSize == previousSize && firstCaret == previousCaret && scale == previousScale) return;
            previousText = text.Text; previousSize = text.RenderSize; previousCaret = firstCaret; previousScale = scale;
            var face = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
            var visible = Rect.Empty;
            for (var line = 0; line < text.LineCount; line++)
            {
                var index = text.GetCharacterIndexFromLineIndex(line);
                if (index < 0) continue;
                var caret = text.GetRectFromCharacterIndex(index);
                if (caret.IsEmpty) continue;
                var content = text.GetLineText(line).TrimEnd('\r', '\n');
                var formatted = new FormattedText(content, text.Language.GetSpecificCulture(), text.FlowDirection,
                    face, text.FontSize, Brushes.Black, scale);
                var ink = formatted.BuildGeometry(new Point()).Bounds;
                // The speaker follows the first line; the tint follows the entire visible block,
                // including wrapped lines. Both use the same actual glyph metrics.
                if (line == 0)
                {
                    var center = caret.Top + (ink.IsEmpty ? caret.Height / 2 : ink.Top + ink.Height / 2);
                    offset.Y = Math.Max(0, text.TranslatePoint(new Point(0, center), row).Y - buttonHeight / 2);
                }
                if (!ink.IsEmpty)
                    visible.Union(new Rect(text.TranslatePoint(new Point(0, caret.Top + ink.Top), surface), new Size(ink.Width, ink.Height)));
            }
            if (!visible.IsEmpty) surface.CenterOn(visible.Top + visible.Height / 2);
        };
    }

    private ContextMenu ReadAloudMenu(TextBox text, string key)
    {
        var read = new MenuItem { Header = "朗读" };
        var menu = new ContextMenu();
        menu.Items.Add(read);
        var repeat = new MenuItem { Header = "再次播放上一段" }; menu.Items.Add(repeat);
        repeat.Click += async (_, _) => { if (lastReading is { } last) await SpeakAsync(last.Text, last.Key, last.Options); };
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "复制", Command = ApplicationCommands.Copy, CommandTarget = text });
        menu.Items.Add(new MenuItem { Header = "全选", Command = ApplicationCommands.SelectAll, CommandTarget = text });
        var selection = "";
        menu.Opened += (_, _) =>
        {
            repeat.IsEnabled = lastReading != null;
            selection = text.SelectedText;
            read.Header = selection.Length > 0 ? "朗读所选文字" : speakingKey == key ? "停止朗读" : "朗读";
        };
        read.Click += async (_, _) => await ReadAloudAsync(key, text.Text, selection);
        return menu;
    }

    // An empty selection reads the whole block, or stops it while it plays.
    private async Task ReadAloudAsync(string key, string text, string selection)
    {
        if (selection.Length == 0 && speakingKey == key) { StopSpeech(); LookupStatus.Text = "发音已停止"; return; }
        await SpeakAsync(selection.Length > 0 ? selection : text, key);
    }

    private void ShowReadAloud()
    {
        foreach (var block in readAloudBlocks) block.Show(speakingKey == block.Key, audioPlaying);
    }
}
