using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Lexi;

internal static partial class Program
{
    private static void CheckSuggestions(MainWindow main)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
        var input = (TextBox)main.FindName("SearchBox");
        var list = (ListBox)main.FindName("Suggestions");
        var progress = (QueryProgressView)main.FindName("QueryProgress");
        var candidates = new[] { "idempotent", "idempotence", "idempotent law", "idempotent element" };
        void Begin(string query, string[] words)
        {
            main.DismissSuggestions();
            input.Text = query;
            list.ItemsSource = words;
            list.Visibility = Visibility.Visible;
        }
        Begin("idempo", candidates);
        Check(main.MoveSuggestion(1) && input.Text == "idempotent" && input.CaretIndex == input.Text.Length && input.SelectionLength == 0,
            "first suggestion fills the editable input with the caret at the end");
        Check(main.MoveSuggestion(1) && input.Text == "idempotence" && ReferenceEquals(list.ItemsSource, candidates) && list.SelectedIndex == 1,
            "browsing suggestions preserves the original candidates instead of searching the preview");
        Check(main.MoveSuggestion(-1) && input.Text == "idempotent", "up returns to the previous suggested word");
        Check(main.MoveSuggestion(-1) && input.Text == "idempo" && list.SelectedIndex == -1 && list.Visibility == Visibility.Visible,
            "up from the first suggestion restores the original typed prefix");
        main.MoveSuggestion(1); main.MoveSuggestion(1);
        main.DismissSuggestions(restoreDraft: true);
        Check(input.Text == "idempo" && list.Visibility == Visibility.Collapsed && list.SelectedIndex == -1,
            "escape dismisses suggestions and restores the draft without querying");
        Begin("idempo", candidates);
        list.SelectedIndex = 1;
        Check(input.Text == "idempotence", "direct row selection also previews the complete spelling");
        Check(main.AcceptSuggestion() && input.Text == "idempotence" && input.CaretIndex == input.Text.Length && list.Visibility == Visibility.Collapsed,
            "accepting a clicked or tab-selected suggestion keeps it ready for editing");
        input.Select(input.Text.Length - 1, 1); input.SelectedText = "y";
        Check(input.Text == "idempotency" && list.SelectedIndex == -1, "accepted suggestions can immediately be edited into a different query");
        Begin("idempo", candidates);
        main.MoveSuggestion(1); main.MoveSuggestion(1);
        input.Select(input.Text.Length - 1, 1); input.SelectedText = "y";
        main.DismissSuggestions(restoreDraft: true);
        Check(input.Text == "idempotency", "editing a preview discards its old draft so Escape cannot undo the user's new spelling");
        Begin("t", ["touch grass", "TEE"]);
        main.MoveSuggestion(1); main.MoveSuggestion(1); main.MoveSuggestion(1);
        Check(input.Text == "TEE" && list.SelectedIndex == 1, "candidate navigation preserves case and stops at the last item");
        main.MoveSuggestion(-1);
        Check(input.Text == "touch grass", "multiword candidates retain their complete spelling");
        main.DismissSuggestions();
        Check(!main.MoveSuggestion(1) && !main.AcceptSuggestion(), "hidden candidates cannot be accepted or previewed");
        Begin("unknown", []);
        Check(!main.MoveSuggestion(1) && !main.AcceptSuggestion(), "empty candidate lists leave ordinary input behavior intact");
        Check(progress.Current == null && ((FrameworkElement)main.FindName("Welcome")).Visibility == Visibility.Visible,
            "suggestion preview and acceptance never start a lookup or leave query history");
        Check(!list.IsTabStop && AutomationProperties.GetHelpText(list).Contains("Enter"),
            "suggestions document keyboard editing and keep Tab available for acceptance");
        main.ShowHistory(trackVisit: false);
    }
}
