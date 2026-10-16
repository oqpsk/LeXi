using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lexi;
using Lexi.Core;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Construct and measure resources only: no visible windows, screenshots,
        // simulated input, application message loop or online provider calls.
        var app = new App(); app.InitializeComponent();
        var main = new MainWindow();
        Measure((FrameworkElement)main.Content, 558, 398);
        Measure((FrameworkElement)main.Content, 718, 618);
        Measure((FrameworkElement)main.Content, 1078, 798);
        var dictionary = new InstalledDictionary("Synthetic dictionary", "en / zh", 42, 1024,
            "synthetic-data", "https://example.org/dictionary", "Test license", "Installed", true, "synthetic");
        var settings = new SettingsWindow(new AppSettings(), [dictionary]);
        Measure((FrameworkElement)settings.Content, 538, 478);
        var tabs = Descendants((DependencyObject)settings.Content).OfType<TabControl>().Single();
        foreach (TabItem tab in tabs.Items)
        {
            tabs.SelectedItem = tab;
            Measure((FrameworkElement)settings.Content, 618, 663);
        }
        var panel = new StackPanel { Width = 480 };
        panel.Children.Add(new TextBox { Text = "中文与 English" });
        panel.Children.Add(new CheckBox { Content = "Enabled", IsChecked = true });
        panel.Children.Add(new ComboBox { ItemsSource = new[] { "One", "Two" }, SelectedIndex = 0 });
        panel.Children.Add(new ComboBoxItem { Content = "Menu item" });
        panel.Children.Add(new ListBoxItem { Content = "Suggestion", IsSelected = true });
        panel.Children.Add(new Expander { Header = "Source", Content = new TextBlock { Text = "Source text" }, IsExpanded = true });
        panel.Children.Add(new TextBox { Style = (Style)app.FindResource("Selectable"), Text = "Read-only text" });
        Measure(panel, 480, 600);
        Console.WriteLine("PASS: WPF resources, window content and control templates construct and measure without displaying windows.");
    }
    private static void Measure(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        foreach (var control in Descendants(element).OfType<Control>()) control.ApplyTemplate();
        if (double.IsNaN(element.DesiredSize.Width) || double.IsNaN(element.DesiredSize.Height)) throw new InvalidOperationException("Invalid layout size.");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
