using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Lexi;

public partial class MainWindow
{
    private string? suggestionDraft;

    private void SetSearchText(string text)
    {
        var wasSuppressed = suppressSuggestions;
        suppressSuggestions = true;
        try
        {
            SearchBox.Text = text;
            SearchBox.Select(text.Length, 0);
        }
        finally { suppressSuggestions = wasSuppressed; }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (Placeholder == null) return;
        Placeholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (suppressSuggestions) return;
        // Real edits start a fresh candidate list; previews keep the original list intact.
        DismissSuggestions();
        if (string.IsNullOrWhiteSpace(SearchBox.Text) && current.Length > 0) ShowHistory(false);
        QueueInputPreview();
        if (!settings.OfflineEnabled || !session.Library.IsCompletedSuccessfully) return;
        var suggestions = session.Suggest(SearchBox.Text);
        Suggestions.ItemsSource = suggestions;
        Suggestions.Visibility = suggestions.Length > 0 && SearchBox.IsKeyboardFocused ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Suggestions_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Suggestions.Visibility != Visibility.Visible || Suggestions.SelectedItem is not string text) return;
        suggestionDraft ??= SearchBox.Text;
        SetSearchText(text);
        QueueInputPreview();
    }

    internal bool MoveSuggestion(int direction)
    {
        if (Suggestions.Visibility != Visibility.Visible || Suggestions.Items.Count == 0) return false;
        var next = Suggestions.SelectedIndex + direction;
        if (next < 0)
        {
            Suggestions.SelectedIndex = -1;
            if (suggestionDraft is { } draft) SetSearchText(draft);
            suggestionDraft = null;
            QueueInputPreview();
        }
        else
        {
            Suggestions.SelectedIndex = Math.Min(next, Suggestions.Items.Count - 1);
            Suggestions.ScrollIntoView(Suggestions.SelectedItem);
        }
        return true;
    }

    internal bool AcceptSuggestion()
    {
        if (Suggestions.Visibility != Visibility.Visible || Suggestions.SelectedItem is not string text) return false;
        SetQuery(text);
        SearchBox.Focus();
        SearchBox.Select(text.Length, 0);
        QueueInputPreview();
        return true;
    }

    internal void DismissSuggestions(bool restoreDraft = false)
    {
        CancelInputPreview();
        var draft = suggestionDraft;
        suggestionDraft = null;
        Suggestions.SelectedIndex = -1;
        Suggestions.Visibility = Visibility.Collapsed;
        if (restoreDraft && draft != null) SetSearchText(draft);
    }

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (inputComposing) return;
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            AcceptSuggestion();
            await SearchAsync(SearchBox.Text);
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key is Key.Down or Key.Up && MoveSuggestion(e.Key == Key.Down ? 1 : -1))
        {
            SearchBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && AcceptSuggestion()) e.Handled = true;
    }

    private void Suggestions_Click(object sender, MouseButtonEventArgs e)
    {
        // Ignore scrollbar/background clicks instead of accepting a previously selected row.
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(Suggestions, source) is ListBoxItem item && item.IsSelected)
            e.Handled = AcceptSuggestion();
    }
}
