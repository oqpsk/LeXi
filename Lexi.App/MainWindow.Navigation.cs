using System.Windows;
using System.Windows.Automation;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    private readonly QueryNavigation navigation = new();

    private void RecordVisit(string query, bool translation = false)
    {
        navigation.RememberScroll(ResultsScroll.VerticalOffset);
        navigation.Visit(query, translation);
        UpdateNavigation();
    }

    private void UpdateNavigation()
    {
        BackButton.IsEnabled = navigation.CanBack;
        ForwardButton.IsEnabled = navigation.CanForward;
        static string Label(QueryPage? page)
        {
            if (page == null) return "";
            if (page.Query.Length == 0) return "查询历史";
            var text = page.Query.Replace("\r", " ").Replace("\n", " ");
            return text.Length > 50 ? text[..50] + "…" : text;
        }
        BackButton.ToolTip = navigation.CanBack ? $"后退 · {Label(navigation.Back)} · Alt+←" : "后退 · Alt+←";
        ForwardButton.ToolTip = navigation.CanForward ? $"前进 · {Label(navigation.Forward)} · Alt+→" : "前进 · Alt+→";
    }

    private async void Back_Click(object sender, RoutedEventArgs e) => await NavigateAsync(-1);
    private async void Forward_Click(object sender, RoutedEventArgs e) => await NavigateAsync(1);
    private async Task NavigateAsync(int direction)
    {
        navigation.RememberScroll(ResultsScroll.VerticalOffset);
        if (navigation.Move(direction) is not { } page) return;
        var revision = navigation.Revision;
        UpdateNavigation();
        if (page.Query.Length == 0) ShowHistory(trackVisit: false);
        else await SearchAsync(page.Query, navigating: true);
        // A newer submission or a reader's scroll always wins over this restoration.
        if (navigation.Revision == revision && ResultsScroll.VerticalOffset == 0)
            ResultsScroll.ScrollToVerticalOffset(page.ScrollOffset);
    }

    private void UpdateCloseAction()
    {
        var action = settings.MinimizeOnClose ? "最小化" : "退出 LeXi";
        CloseButton.ToolTip = action + " · 可在设置中更改";
        AutomationProperties.SetName(CloseButton, action);
    }

    internal void ExitApplication() { quit = true; Close(); }
    internal void OpenSettings()
    {
        Restore();
        if (OwnedWindows.OfType<SettingsWindow>().FirstOrDefault() is { } dialog) { dialog.Activate(); return; }
        Settings_Click(this, new RoutedEventArgs());
    }
}
