using System.Windows;
using System.Windows.Automation;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    private void UpdateNavigation(NavigationView navigation)
    {
        BackButton.IsEnabled = navigation.CanBack;
        ForwardButton.IsEnabled = navigation.CanForward;
        BackButton.ToolTip = navigation.CanBack ? $"后退 · {navigation.BackLabel} · Alt+←" : "后退 · Alt+←";
        ForwardButton.ToolTip = navigation.CanForward ? $"前进 · {navigation.ForwardLabel} · Alt+→" : "前进 · Alt+→";
    }

    private async void Back_Click(object sender, RoutedEventArgs e) => await NavigateAsync(-1);
    private async void Forward_Click(object sender, RoutedEventArgs e) => await NavigateAsync(1);
    private async Task NavigateAsync(int direction)
    {
        if (session.Move(direction, ResultsScroll.VerticalOffset) is not { } page) return;
        var revision = session.Navigation.Revision;
        if (page.Query.Length == 0) ShowHistory(trackVisit: false);
        else await SearchAsync(page.Query, navigating: true);
        // A newer submission or a reader's scroll always wins over this restoration.
        if (session.Navigation.Revision == revision && ResultsScroll.VerticalOffset == 0)
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
        skipSummonedLookup = !IsActive; CancelSummonedLookup();
        Restore();
        if (OwnedWindows.OfType<SettingsWindow>().FirstOrDefault() is { } dialog) { dialog.Activate(); return; }
        Settings_Click(this, new RoutedEventArgs());
    }
}
