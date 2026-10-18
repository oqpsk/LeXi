using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Lexi;

internal static partial class Program
{
    private static void CheckWindowNavigation(MainWindow main, SettingsWindow settings)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
        var toolbar = (Border)main.FindName("WindowToolbar");
        var queryBar = (Border)main.FindName("QueryBar");
        var drag = (Border)main.FindName("WindowDragArea");
        var back = (Button)main.FindName("BackButton"); var forward = (Button)main.FindName("ForwardButton");
        var close = (Button)main.FindName("CloseButton");
        var buttons = Descendants(toolbar).OfType<Button>().ToArray();
        foreach (var width in new[] { 558, 718, 1078 })
        {
            Measure((FrameworkElement)main.Content, width, 398);
            var rowBottom = toolbar.TranslatePoint(new Point(0, toolbar.ActualHeight), main).Y;
            var searchTop = queryBar.TranslatePoint(new Point(0, 0), main).Y;
            Check(buttons.Length == 4 && buttons.All(b => b.ActualWidth == 32 && b.ActualHeight == 32) &&
                buttons.Select(b => b.TranslatePoint(new Point(0,0),toolbar).Y).Distinct().Count() == 1 &&
                rowBottom <= searchTop && drag.ActualWidth >= 270,
                $"window controls align above the query and leave a continuous drag area at {width} DIP");
        }
        Check(Theme.CanDragFrom(drag) && drag.Cursor == Cursors.SizeAll && drag.ToolTip.ToString()!.Contains("拖动") &&
            buttons.All(b => !Theme.CanDragFrom(b)), "dedicated drag strip is discoverable and excludes every window action");
        Check(!back.IsEnabled && !forward.IsEnabled && AutomationProperties.GetName(back) == "后退" &&
            AutomationProperties.GetName(forward) == "前进", "initial navigation controls expose disabled accessible states");
        Check(((Panel)main.FindName("SearchActions")).Children.Count == 1 &&
            AutomationProperties.GetName(close) == "最小化", "query row retains submit without duplicate progress while close describes its configured action");
        foreach (var name in new[] { "BackIcon", "ForwardIcon" })
            Check(!Theme.Icon(name).Data.Bounds.IsEmpty, name + " is a vector icon");
        var tabs = Contents(settings).OfType<TabControl>().Single();
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => (string)t.Header == "窗口");
        Measure((FrameworkElement)settings.Content, 538, 478);
        var choice = Descendants((DependencyObject)settings.Content).OfType<ComboBox>().Single();
        Check(choice.SelectedIndex == 0 && choice.Items.Count == 2 &&
            AutomationProperties.GetName(choice) == "窗口关闭行为", "window settings expose explicit minimize and exit choices with the compatible default");
        using var image = TrayIcon.LoadImage();
        Check(image.Width >= 16 && image.Height >= 16 && image.Handle != IntPtr.Zero,
            "tray branding loads from the packaged icon and outlives the resource stream");
        // Command dispatch only: no notification icon, context menu display or desktop input.
        int opened = 0, configured = 0, exited = 0;
        using var menu = TrayIcon.CreateMenu(() => opened++, () => configured++, () => exited++);
        menu.Items[0].PerformClick(); menu.Items[1].PerformClick(); menu.Items[3].PerformClick();
        Check(menu.Items.Count == 4 && opened == 1 && configured == 1 && exited == 1,
            "tray open, settings and unconditional exit dispatch independently without displaying UI");
    }
}
