using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Lexi;
using Lexi.Core;

internal static partial class Program
{
    private static void CheckHistory(MainWindow main)
    {
        void Check(bool ok,string name) { if(!ok) throw new Exception("FAIL: "+name); Console.WriteLine("PASS: "+name); }
        main.ShowHistory();
        main.RenderHistory([new("a long dictionary phrase that needs truncation at a narrow width",false,DateTimeOffset.UtcNow),
            new("A complete paragraph.\n\nSecond paragraph preserved.",true,DateTimeOffset.UtcNow)]);
        var list=(StackPanel)main.FindName("HistoryList");
        var row=(Grid)list.Children[0];
        var open=(Button)row.Children[0]; var remove=(Button)row.Children[1];
        Check(row.Children.OfType<Button>().Count()==2 && AutomationProperties.GetName(open).StartsWith("重新查询") &&
            AutomationProperties.GetName(remove).StartsWith("删除历史") && !Theme.CanDragFrom(open) && !Theme.CanDragFrom(remove),
            "history offers separate accessible query and delete buttons outside window drag regions");
        foreach(var width in new[]{558,718,1078})
        {
            Measure((FrameworkElement)main.Content,width,618);
            var right=open.TranslatePoint(new Point(open.ActualWidth,0),row).X;
            var left=remove.TranslatePoint(new Point(0,0),row).X;
            Check(open.ActualWidth>200 && right<=left && row.ActualHeight>=44 && remove.ActualHeight>=32,
                $"history rows retain readable text and separate delete targets at {width+2} DIP");
        }
        var text=Contents(open).OfType<TextBlock>().First(t=>t.Text.StartsWith("a long"));
        Check(text.TextTrimming==TextTrimming.CharacterEllipsis && text.FontSize>=17 && ((HistoryEntry)open.Tag).Query==open.ToolTip.ToString(),
            "history uses a readable one-line preview and retains the full query on the button");
        Check((string)((Button)((Grid)list.Children[1]).Children[0]).ToolTip=="A complete paragraph.\n\nSecond paragraph preserved.",
            "multiline translation history retains its exact paragraphs for requery");
        var progress=(QueryProgressView)main.FindName("QueryProgress");
        var oldRequest=progress.Begin(QueryProgressState.Dictionary(true,true));
        ((FrameworkElement)main.FindName("EntryPanel")).Visibility=Visibility.Visible;
        main.ShowHistory();
        progress.Update(oldRequest,QueryProgressState.Dictionary(true,true));
        Check(progress.Current==null && progress.Visibility==Visibility.Hidden && ((FrameworkElement)main.FindName("EntryPanel")).Visibility==Visibility.Collapsed &&
            ((FrameworkElement)main.FindName("Welcome")).Visibility==Visibility.Visible,
            "returning home clears results and rejects late progress from the previous query");
        main.RenderHistory([]);
        Check(list.Children.Count==0 && ((FrameworkElement)main.FindName("HistoryMessage")).Visibility==Visibility.Visible,
            "empty history has a useful empty state without canned example rows");
    }
}
