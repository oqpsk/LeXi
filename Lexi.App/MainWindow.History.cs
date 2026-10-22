using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    private int historyLimit = QueryHistory.HomePageSize;
    private void MoreHistory_Click(object sender, RoutedEventArgs e) { historyLimit += QueryHistory.HomePageSize; RenderHistory(history.Entries); }
    private QueryHistory history => session.History;
    private void HistoryHome_Click(object sender, RoutedEventArgs e) => ShowHistory();
    internal void ShowHistory(bool clearInput = true, bool trackVisit = true)
    {
        StopSpeech(); ResetSenseDetails(); ResetLexicalPopup();
        session.ShowHome(trackVisit, ResultsScroll.VerticalOffset);
        Suggestions.Visibility = Visibility.Collapsed;
        if (clearInput) SetQuery("");
        EntryPanel.Visibility = Visibility.Collapsed; Welcome.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Collapsed; RefreshButton.Visibility = Visibility.Collapsed; ResultsScroll.ScrollToTop();
        RenderHistory(history.Entries);
        SearchBox.Focus();
    }
    internal void RenderHistory(IReadOnlyList<HistoryEntry> entries)
    {
        HistoryList.Children.Clear();
        HistoryMessage.Text = history.Error.Length > 0 ? history.Error : "暂无查询历史。查过的词语和文本会显示在这里。";
        HistoryMessage.Visibility = entries.Count == 0 || history.Error.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        MoreHistory.Visibility = entries.Count > historyLimit ? Visibility.Visible : Visibility.Collapsed;
        foreach (var entry in entries.Take(historyLimit))
        {
            var row = new Grid { Margin = new(0,0,0,5) };
            row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var content = new Grid();
            content.ColumnDefinitions.Add(new()); content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            content.Children.Add(new TextBlock { Text = entry.Preview, FontSize = 17, TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new(0,0,12,0) });
            var kind = new TextBlock { Text = entry.Kind, Foreground = muted, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(kind,1); content.Children.Add(kind);
            var open = new Button { Content = content, Tag = entry, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new(12,10,12,10), MinHeight = 44, ToolTip = entry.Query, Background = Theme.Brush("PanelSurface") };
            AutomationProperties.SetName(open,"重新查询：" + entry.Preview);
            open.Click += async (_, _) => await SearchAsync(entry.Query);
            row.Children.Add(open);
            // Sibling buttons: deleting a record cannot bubble into query submission.
            var remove = new Button { Content = Theme.Icon("CloseIcon"), Tag = entry, Style = (Style)FindResource("IconButton"),
                Margin = new(6,0,0,0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "删除这条历史" };
            AutomationProperties.SetName(remove,"删除历史：" + entry.Preview);
            remove.Click += (_, _) =>
            {
                session.RemoveHistory(entry); RenderHistory(history.Entries);
                if (HistoryList.Children.OfType<Grid>().FirstOrDefault()?.Children.OfType<Button>().FirstOrDefault() is { } next) next.Focus();
                else SearchBox.Focus();
            };
            Grid.SetColumn(remove,1); row.Children.Add(remove);
            HistoryList.Children.Add(row);
        }
    }
}
