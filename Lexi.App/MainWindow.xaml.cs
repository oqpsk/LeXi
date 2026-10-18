using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow : Window
{
    private readonly QuerySession session = new(App.Paths, textCredentials: url => new TextProviderKeys(App.UserRoot).Read(url));
    private AppSettings settings => session.Settings;
    private bool quit;
    private readonly QueryViewDelivery viewDelivery;
    private readonly WindowPlacement windowPlacement;
    private bool suppressSuggestions;
    private string current => session.Current;
    private readonly Brush muted = Theme.Brush("Muted");
    private readonly Brush accentBrush = Theme.Brush("Accent");
    public MainWindow()
    {
        InitializeComponent();
        QueryProgress.RetryRequested += (_, _) => Retry_Click(this, new RoutedEventArgs());
        InitializeInputPreview();
        SourceInitialized += (_, _) => InitializeInstantLookup();
        shown = session.View;
        viewDelivery = new(Dispatcher, Render);
        session.Changed += Render;
        FocusManager.SetFocusedElement(this, SearchBox);
        RenderHistory(history.Entries);
        Theme.ApplyChrome(this);
        windowPlacement = new(this, Path.Combine(App.UserRoot, "window-placement.json"));
        UpdateOnlineButton(); UpdateNavigation(session.View.Navigation); UpdateCloseAction();
        LocationChanged += (_, _) => EtymologyPopup.IsOpen = false;
        SizeChanged += (_, _) => EtymologyPopup.IsOpen = false;
        Deactivated += (_, _) => { CancelSummonedLookup(); CancelSearchFocus(); CancelInputPreview(); EtymologyPopup.IsOpen = false; };
        ContentRendered += (_, _) => QueueSearchFocus();
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) CancelSearchFocus(); else QueueSearchFocus(); };
        PreviewMouseDown += (_, _) => { CancelSummonedLookup(); CancelSearchFocus(); };
        ResultsScroll.ScrollChanged += (_, e) => { if (e.VerticalChange != 0) EtymologyPopup.IsOpen = false; };
        Loaded += async (_, _) => { FocusSearch(); QueueSearchFocus(); await session.InitializeAsync(); };
        Closing += (_, e) =>
        {
            if (!quit && settings.MinimizeOnClose && !Dispatcher.HasShutdownStarted) { e.Cancel = true; WindowState = WindowState.Minimized; }
        };
        Closed += (_, _) => { focusClosed = true; CancelSummonedLookup(); instantLookup?.Dispose(); CancelInputPreview(); CancelSearchFocus(); session.Cancel(); StopSpeech(); speech.Dispose(); session.Dispose(); };
    }
    public void Restore()
    {
        if (WindowState == WindowState.Minimized) WindowState = windowPlacement.RestoreState;
        Show(); Activate(); FocusSearch(); QueueSearchFocus();
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Activated(object? sender, EventArgs e) { FocusSearch(); QueueSearchFocus(); LookUpSummoned(); }
    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (inputComposing) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.Modifiers == ModifierKeys.Alt && key is Key.Left or Key.Right)
        { e.Handled = true; await NavigateAsync(key == Key.Left ? -1 : 1); return; }
        if (e.Key == Key.Escape)
        {
            if (EtymologyPopup.IsOpen) DismissEtymology();
            else if (Suggestions.Visibility == Visibility.Visible || InputPreviewCard.Visibility == Visibility.Visible) DismissSuggestions(restoreDraft: true);
            else WindowState = WindowState.Minimized;
            e.Handled = true;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D && shown.Kind != QueryViewKind.Home && speechText.Length > 0)
        { e.Handled = true; if (!e.IsRepeat) Speech_Click(sender, e); return; }
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.C)
        { e.Handled = true; CopySummary(); return; }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L) { FocusSearch(); e.Handled = true; }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Q) { ExitApplication(); e.Handled = true; }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.OemComma) { e.Handled = true; Settings_Click(sender, e); }
    }
    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync(SearchBox.Text);
    private async void Refresh_Click(object sender, RoutedEventArgs e) { if (current.Length > 0) await SearchAsync(current, true); }
    private void SetQuery(string query)
    {
        DismissSuggestions();
        SetSearchText(query);
    }
    private void UpdateOnlineButton()
    {
        OnlineToggle.IsChecked = settings.Online;
        OnlineToggle.Content = ControlText.OnlineLabel(settings.Online);
        OnlineToggle.ToolTip = ControlText.OnlineTooltip(settings.Online);
        RefreshButton.IsEnabled = settings.Online;
        RefreshButton.ToolTip = ControlText.RegenerateTooltip(settings.Online);
    }
    private void Online_Click(object sender, RoutedEventArgs e)
    {
        session.SetOnline(OnlineToggle.IsChecked == true);
        UpdateOnlineButton();
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        CancelSummonedLookup(); instantLookup?.Discard();
        StopSpeech();
        var previous = session.QuerySettingsIdentity();
        var library = session.Library;
        var dialog = new SettingsWindow(settings, library.IsCompletedSuccessfully ? library.Result.Dictionaries : DictionaryCatalog.InspectAll(App.Paths.DataRoot), null, session: session) { Owner = this };
        if (dialog.ShowDialog() == true || dialog.DataImported)
        {
            session.SettingsSaved(previous);
            UpdateOnlineButton(); UpdateCloseAction();
            if (!settings.OfflineEnabled) Suggestions.Visibility = Visibility.Collapsed;
        }
        CancelSummonedLookup(); instantLookup?.Discard();
        FocusSearch();
    }
    private async void Retry_Click(object sender, RoutedEventArgs e) { StopSpeech(); await session.RetryFailedAsync(); }
    private async Task SearchAsync(string raw, bool force = false, bool navigating = false, string? context = null)
    {
        // Speech always belongs to the result being replaced; invalid input leaves it untouched.
        if (Queries.Validate(raw) == null) StopSpeech();
        CancelSummonedLookup();
        if (force && raw == current && !navigating) await session.RegenerateAsync();
        else await session.SubmitAsync(raw, force, navigating, ResultsScroll.VerticalOffset, context);
    }

    // ---- Rendering of Core query views ----
    // Each section redraws only when its Core value changes, so streaming updates keep the
    // reader's disclosures, scroll position and original-source expansion.
    private QueryView shown;
    private long progressRequest;
    private void Render(QueryView view)
    {
        if (!Dispatcher.CheckAccess()) { viewDelivery.Post(view); return; }
        if (focusClosed) return;
        if (view.Revision <= shown.Revision) return;
        var previous = shown; shown = view;
        bool Changed<T>(Func<QueryView, T> part) => !Equals(part(previous), part(view));
        var fresh = view.Request != previous.Request;
        if (fresh || Changed(v => v.Status)) LookupStatus.Text = view.Status;
        if (fresh || Changed(v => v.Progress)) FooterActivity.Activity = view.Progress?.Activity ?? QueryActivity.Idle;
        if (Changed(v => v.Navigation)) UpdateNavigation(view.Navigation);
        if (view.ProgressGeneration != previous.ProgressGeneration)
        {
            if (view.Progress == null) QueryProgress.Reset();
            else progressRequest = QueryProgress.Begin(view.Progress);
        }
        else if (view.Progress != null && Changed(v => v.Progress)) QueryProgress.Update(progressRequest, view.Progress);
        if (fresh && view.Kind != QueryViewKind.Home)
        {
            speechText = view.SpeechText;
            ResetSenseDetails(); ResetLexicalPopup();
            SetQuery(view.Input);
            Welcome.Visibility = Visibility.Collapsed; EntryPanel.Visibility = Visibility.Visible;
            RefreshButton.Visibility = Visibility.Visible; RefreshButton.IsEnabled = view.CanRegenerate;
            Headword.Text = view.Heading; Headword.FontSize = view.Kind == QueryViewKind.Translation ? 28 : 30;
            ResultsScroll.ScrollToTop();
        }
        RetryButton.Visibility = view.CanRetry ? Visibility.Visible : Visibility.Collapsed;
        if (Changed(v => v.Pronunciation)) RenderPronunciation(view.Pronunciation);
        if (Changed(v => v.WordForms)) RenderFormRows(view.WordForms);
        if (Changed(v => v.Etymology)) RenderEtymology(view.Etymology);
        if (fresh || Changed(v => v.QuickRead) || previous.Progress?.Busy != view.Progress?.Busy) RenderQuickRead(view.QuickRead);
        if (view.Kind == QueryViewKind.Translation)
        {
            if (fresh) { LocalPanel.Children.Clear(); OnlinePanel.Children.Clear(); }
            if (fresh || Changed(v => v.Translation)) RenderTranslation(view.Translation);
            return;
        }
        if (fresh && previous.Kind == QueryViewKind.Translation) AiPanel.Children.Clear();
        if (Changed(v => v.Local) || Changed(v => v.LocalHint)) RenderLocal(view.Local, view.LocalHint);
        if (Changed(v => v.Online) || Changed(v => v.OnlineHint)) RenderOnline(view.Online, view.OnlineHint, view.Answers is null or { Empty: true });
        if (Changed(v => v.Answers)) RenderAnswerCard(view.Answers);
    }
    private void RenderPronunciation(SavedPronunciation pronunciation)
    {
        Phonetic.Text = pronunciation.Text; PhoneticLabel.Text = pronunciation.Label;
        Phonetic.ToolTip = pronunciation.Attribution.Length > 0 ? pronunciation.Attribution : null;
        PhoneticLabel.ToolTip = pronunciation.LabelAttribution.Length > 0 ? pronunciation.LabelAttribution : null;
    }
    private TextBlock Hint(string text, Brush? brush = null) => new() { Text = text, Foreground = brush ?? muted, FontSize = 14, Margin = new(0,4,0,12), TextWrapping = TextWrapping.Wrap };
    private TextBox Selectable(string text, double size = 18)
    {
        var block = new TextBox { Text = text, Style = (Style)FindResource("Selectable"), FontSize = size, Margin = new(0,4,0,8) };
        TextBlock.SetLineHeight(block, size * 1.55);
        TextBlock.SetLineStackingStrategy(block, LineStackingStrategy.BlockLineHeight);
        return block;
    }
    private StackPanel Card(Panel parent, string title, string source, bool accent = false)
    {
        var content = new StackPanel();
        var header = new Grid { Margin = new(0,0,0,10) };
        header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = accent ? accentBrush : Foreground, Margin = new(0,0,10,0) });
        var tag = new TextBlock { Text = source, FontSize = 13, Foreground = muted, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(tag,1); header.Children.Add(tag);
        if (title.Length > 0) content.Children.Add(header);
        parent.Children.Add(new Border { Background = accent ? Theme.Brush("PanelSurface") : Brushes.Transparent, CornerRadius = new(accent ? 8 : 0), BorderBrush = Theme.Brush("Stroke"), BorderThickness = accent ? new(1) : new(0,0,0,1), Padding = accent ? new(16,14,16,12) : new(0,12,0,14), Margin = new(0,0,0,12), Child = content });
        return content;
    }
    private void RenderLocal(LocalSourceView[] sources, string hint)
    {
        LocalPanel.Children.Clear();
        if (hint.Length > 0) { LocalPanel.Children.Add(Hint(hint)); return; }
        foreach (var source in sources)
        {
            var card = Card(LocalPanel, source.Name, "离线原文");
            if (source.Headword.Length > 0) card.Children.Add(Hint(source.Headword));
            if (!string.IsNullOrWhiteSpace(source.Chinese)) card.Children.Add(Selectable(source.Chinese));
            if (!string.IsNullOrWhiteSpace(source.English)) card.Children.Add(Selectable(source.English));
            if (!string.IsNullOrWhiteSpace(source.Original)) card.Children.Add(new Expander { Header = "完整原始条目", Content = Selectable(source.Original, 16), Margin = new(0,10,0,4) });
            var link = new Button { Content = "词典来源 ↗", FontSize = 14, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Left, Padding = new(0,5,0,0) };
            link.Click += (_, _) => OpenSource(source.SourceUrl); card.Children.Add(link);
            card.Children.Add(Hint(source.Attribution));
        }
    }
    private void RenderOnline(OnlineSourceView? entry, string hint, bool expanded)
    {
        OnlinePanel.Children.Clear();
        if (entry == null) { if (hint.Length > 0) OnlinePanel.Children.Add(Hint(hint)); return; }
        var stack = new StackPanel { Margin = new(0,10,0,0) };
        foreach (var sense in entry.Senses)
        {
            stack.Children.Add(new TextBlock { Text = sense.Label, Foreground = accentBrush, FontSize = 15, Margin = new(0,7,0,3) });
            stack.Children.Add(Selectable(sense.Definition));
            foreach (var example in sense.Examples) { var text = ExampleText(example.Text, example.Chinese); text.Foreground = muted; stack.Children.Add(text); }
        }
        var source = new Button { Content = "查看 Wiktionary 来源 ↗", HorizontalAlignment = HorizontalAlignment.Left, Foreground = accentBrush, Padding = new(0,8,0,8) };
        source.Click += (_, _) => OpenSource(entry.Url);
        stack.Children.Add(source);
        stack.Children.Add(Hint(entry.Footer));
        OnlinePanel.Children.Add(new Expander { Header = entry.Header, Content = stack, IsExpanded = expanded, Foreground = Foreground, Margin = new(2,5,2,10) });
    }
    private static void OpenSource(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { }
    }
}
