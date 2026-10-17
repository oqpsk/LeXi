using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow : Window
{
    private readonly Task<OfflineLibrary> dictionaryTask;
    private readonly OnlineDictionary web = new();
    private readonly Cache cache = new(App.UserRoot);
    private readonly CodexDictionary codex = new(Path.Combine(App.Root, "assets"), App.UserRoot);
    private readonly ClaudeDictionary claude = new(Path.Combine(App.Root, "assets"), App.UserRoot);
    private readonly ClaudeDictionary secondaryClaude = new(Path.Combine(App.Root, "assets"), App.UserRoot);
    private readonly AppSettings settings = AppSettings.Load(App.UserRoot);
    private CancellationTokenSource? lookup;
    private bool quit;
    private bool suppressSuggestions;
    private string current = "";
    private readonly Brush muted = Theme.Brush("Muted");
    private readonly Brush accentBrush = Theme.Brush("Accent");
    public MainWindow()
    {
        InitializeComponent();
        Theme.ApplyChrome(this);
        UpdateOnlineButton();
        dictionaryTask = Task.Run(() => new OfflineLibrary(Path.Combine(App.Root, "data")));
        Loaded += async (_, _) =>
        {
            SearchBox.Focus();
            UpdatePreparedModel();
            try
            {
                await dictionaryTask;
                UpdateReadyStatus();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { LookupStatus.Text = "离线词库未能加载，请在设置中检查词库状态。"; }
        };
        Closing += (_, e) =>
        {
            if (!quit && !Dispatcher.HasShutdownStarted) { e.Cancel = true; WindowState = WindowState.Minimized; }
        };
        Closed += (_, _) => { lookup?.Cancel(); StopSpeech(); speech.Dispose(); pronunciations.Dispose(); claude.Dispose(); secondaryClaude.Dispose(); web.Dispose(); if (dictionaryTask.IsCompletedSuccessfully) dictionaryTask.Result.Dispose(); };
    }
    public void Restore()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show(); Activate(); FocusSearch();
    }
    private void FocusSearch() { SearchBox.Focus(); SearchBox.SelectAll(); }
    private void Hide_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Window_Activated(object? sender, EventArgs e) => Dispatcher.BeginInvoke(FocusSearch);
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (Suggestions.Visibility == Visibility.Visible) Suggestions.Visibility = Visibility.Collapsed;
            else WindowState = WindowState.Minimized;
            e.Handled = true;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L) { FocusSearch(); e.Handled = true; }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Q) { quit = true; Close(); e.Handled = true; }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.OemComma) { e.Handled = true; Settings_Click(sender, e); }
    }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (Placeholder == null) return;
        Placeholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (suppressSuggestions || !settings.OfflineEnabled || dictionaryTask?.IsCompletedSuccessfully != true) return;
        var suggestions = dictionaryTask.Result.Suggest(SearchBox.Text, settings);
        Suggestions.ItemsSource = suggestions;
        Suggestions.Visibility = suggestions.Length > 0 && SearchBox.IsKeyboardFocused ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { e.Handled = true; await SearchAsync(SearchBox.Text); }
        if (e.Key == Key.Down && Suggestions.Visibility == Visibility.Visible && Suggestions.Items.Count > 0)
        { Suggestions.SelectedIndex = 0; Suggestions.Focus(); (Suggestions.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus(); e.Handled = true; }
    }
    private async void Suggestions_Click(object sender, MouseButtonEventArgs e)
    { if (Suggestions.SelectedItem is string text) { SetQuery(text); await SearchAsync(text); } }
    private async void Suggestions_KeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter && Suggestions.SelectedItem is string text) { e.Handled = true; SetQuery(text); await SearchAsync(text); } }
    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync(SearchBox.Text);
    private async void Example_Click(object sender, RoutedEventArgs e) { var text = (string)((Button)sender).Tag; SetQuery(text); await SearchAsync(text); }
    private async void Refresh_Click(object sender, RoutedEventArgs e) { if (current.Length > 0) await SearchAsync(current, true); }
    private void SetQuery(string query)
    { suppressSuggestions = true; SearchBox.Text = query; suppressSuggestions = false; Suggestions.Visibility = Visibility.Collapsed; }
    private void UpdateOnlineButton()
    {
        OnlineToggle.IsChecked = settings.Online;
        OnlineToggle.Content = settings.Online ? "在线补充" : "离线查询";
        OnlineToggle.ToolTip = settings.Online ? "在线补充已开启 · 点击关闭，仅使用本地词库与缓存" : "仅离线 · 点击开启在线词典与模型补充";
        RefreshButton.IsEnabled = settings.Online;
        RefreshButton.ToolTip = settings.Online ? "重新查询并生成结果，跳过本次查询的已有缓存" : "开启在线补充后可重新生成";
    }
    private void UpdateReadyStatus()
    {
        if (current.Length > 0) return;
        LookupStatus.Text = !settings.OfflineEnabled ? "离线词典已停用 · 可在设置中启用" :
            dictionaryTask.IsCompletedSuccessfully && dictionaryTask.Result.Dictionaries.Any(d => d.Available && !(settings.DisabledDictionaries ?? []).Contains(d.Id)) ?
                "准备就绪" : "暂无可用离线词库 · 可在设置中检查";
    }
    private async void Online_Click(object sender, RoutedEventArgs e)
    {
        settings.Online = OnlineToggle.IsChecked == true; SaveSettings(); UpdateOnlineButton();
        lookup?.Cancel();
        UpdatePreparedModel();
        if (current.Length > 0) await SearchAsync(current);
        else UpdateReadyStatus();
    }
    private void UpdatePreparedModel()
    {
        if (settings.Online && settings.AutoAi && settings.Provider == "claude") claude.Prepare(settings.Model);
        else claude.ReleasePrepared();
        var second = LookupModel.Secondary(settings);
        if (settings.Online && settings.AutoAi && settings.ParallelLookup && second.Provider == "claude" && second != LookupModel.Primary(settings)) secondaryClaude.Prepare(second.Model);
        else secondaryClaude.ReleasePrepared();
    }
    private void SaveSettings()
    {
        try { settings.Save(App.UserRoot); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LookupStatus.Text = "设置暂时无法保存，请检查文件夹写入权限。"; }
    }
    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        StopSpeech();
        var previous = JsonSerializer.Serialize(settings, Json.Options);
        var dialog = new SettingsWindow(settings, dictionaryTask.IsCompletedSuccessfully ? dictionaryTask.Result.Dictionaries : DictionaryCatalog.InspectAll(Path.Combine(App.Root, "data"))) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            SaveSettings(); UpdateOnlineButton(); UpdateReadyStatus();
            if (!settings.OfflineEnabled) Suggestions.Visibility = Visibility.Collapsed;
            if (previous != JsonSerializer.Serialize(settings, Json.Options))
            {
                lookup?.Cancel();
                UpdatePreparedModel();
                if (current.Length > 0) await SearchAsync(current);
            }
        }
        FocusSearch();
    }
    private async Task SearchAsync(string raw, bool force = false)
    {
        var validation = Queries.Validate(raw);
        if (validation != null) { LookupStatus.Text = validation; return; }
        var knownTerm = raw.Length <= 100 && !raw.Contains('\n') && dictionaryTask.IsCompletedSuccessfully &&
            dictionaryTask.Result.Lookup(Queries.Normalize(raw), settings).Length > 0;
        if (Queries.IsText(raw, knownTerm))
        { await TranslateTextAsync(raw.Trim(), force); return; }
        var query = Queries.Normalize(raw);
        StopSpeech();
        lookup?.Cancel(); lookup?.Dispose(); lookup = new();
        speechText = raw.Trim();
        var token = lookup.Token;
        current = query;
        ResetSenseDetails();
        SetQuery(raw.Trim());
        Welcome.Visibility = Visibility.Collapsed; EntryPanel.Visibility = Visibility.Visible;
        RefreshButton.Visibility = Visibility.Visible; RefreshButton.IsEnabled = settings.Online;
        Headword.Text = raw.Trim(); Headword.FontSize = 30; Phonetic.Text = ""; Phonetic.ToolTip = null;
        PhoneticLabel.Text = ""; PhoneticLabel.ToolTip = null; sourcePronunciation = false;
        LocalPanel.Children.Clear(); OnlinePanel.Children.Clear(); AiPanel.Children.Clear(); FormsPanel.Children.Clear();
        ResultsScroll.ScrollToTop(); LookupStatus.Text = "查询本地词库…";
        var modelsEnabled = settings.Online && (settings.AutoAi || force);
        var mergeEnabled = modelsEnabled && settings.MergeSenses;
        var initialProgress = QueryProgressState.Dictionary(modelsEnabled, mergeEnabled);
        var request = QueryProgress.Begin(initialProgress);
        var watch = Stopwatch.StartNew();
        var notes = new List<string>();
        var sourceIncomplete = false;
        LocalHit[] local = [];
        try
        {
            try
            {
                if (settings.OfflineEnabled)
                {
                    var dictionary = await dictionaryTask;
                    token.ThrowIfCancellationRequested();
                    local = dictionary.Lookup(query, settings);
                    RenderWordForms(query, dictionary.Forms(query, settings));
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { notes.Add("本地词库不可用"); sourceIncomplete = true; }
            token.ThrowIfCancellationRequested();
            RenderLocal(local, query);
            var pronunciationTask = LoadPronunciationAsync(query, local, token, force);
            var online = cache.Read<OnlineEntry>("online-v2", query);
            if (online != null) RenderOnline(online, true);
            var evidence = DictionaryEvidence.Combine(query, local, online);
            var coordinator = CreateCoordinator();
            var cachedAnswers = coordinator.Cached(query, evidence, settings);
            if (cachedAnswers.Groups.Length > 0) RenderAnswers(cachedAnswers, evidence, !settings.Online);
            if (!settings.Online)
            {
                LookupStatus.Text = "离线模式 · 使用本地词库与已缓存释义";
                QueryProgress.Update(request, QueryProgressState.SourcesFinished(true, sourceIncomplete)); return;
            }
            var fresh = !force && online != null && DateTimeOffset.UtcNow - online.FetchedAt < TimeSpan.FromDays(1);
            if (!fresh && (force || !Queries.HasChinese(query) || !local.Any(h => h.Dictionary.AllowAiEvidence)))
            {
                LookupStatus.Text = "本地查询完成 · 正在查在线词典…";
                try
                {
                    var fetched = await web.LookupAsync(query, token);
                    token.ThrowIfCancellationRequested();
                    if (fetched != null) { online = fetched; await TryCacheAsync("online-v2", query, online); }
                    else if (online == null) notes.Add("在线词典未收录");
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { notes.Add("在线查询超时"); sourceIncomplete = true; }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException) { notes.Add("在线来源暂不可用"); sourceIncomplete = true; }
            }
            token.ThrowIfCancellationRequested();
            if (online != null) RenderOnline(online, fresh || DateTimeOffset.UtcNow - online.FetchedAt > TimeSpan.FromMinutes(1));
            else { OnlinePanel.Children.Clear(); if (local.Length == 0) OnlinePanel.Children.Add(Hint("Wiktionary 暂无可用词条。")); }
            evidence = DictionaryEvidence.Combine(query, local, online);
            AiPanel.Children.Clear();
            cachedAnswers = coordinator.Cached(query, evidence, settings);
            if (cachedAnswers.Groups.Length > 0) RenderAnswers(cachedAnswers, evidence, !modelsEnabled);
            if (!modelsEnabled)
            {
                await pronunciationTask; token.ThrowIfCancellationRequested();
                LookupStatus.Text = Status(notes, "查询完成 · 点击“重新生成”获取双语释义");
                QueryProgress.Update(request, QueryProgressState.SourcesFinished(false, sourceIncomplete)); return;
            }
            QueryProgress.Update(request, QueryProgressState.Updating(new(LookupModel.Selected(settings).Select(m => new ModelAnswer(m, ModelPhase.Pending)).ToArray(), []), mergeEnabled));
            long? firstDefinitionMs = null;
            var finished = false;
            try
            {
                var progress = new Progress<LookupSnapshot>(snapshot =>
                {
                    if (finished || token.IsCancellationRequested || current != query) return;
                    if (snapshot.Groups.Length > 0) firstDefinitionMs ??= watch.ElapsedMilliseconds;
                    RenderAnswers(snapshot, evidence, false);
                    QueryProgress.Update(request, QueryProgressState.Updating(snapshot, mergeEnabled));
                    LookupStatus.Text = snapshot.Merging ? "释义已显示 · 正在合并同义项…" : ModelStatus(snapshot);
                });
                var result = await coordinator.LookupAsync(query, evidence, settings, force, token, progress);
                await pronunciationTask;
                token.ThrowIfCancellationRequested();
                finished = true;
                if (result.Groups.Length > 0) firstDefinitionMs ??= watch.ElapsedMilliseconds;
                RenderAnswers(result, evidence);
                QueryProgress.Update(request, QueryProgressState.Finished(result, mergeEnabled, sourceIncomplete));
                var failures = result.Answers.Count(a => a.Phase == ModelPhase.Failed);
                var status = failures > 0 ? $"{failures} 个模型未完成 · 其余结果已保留" : "查询完成";
                if (!string.IsNullOrEmpty(result.MergeNote)) status += " · " + result.MergeNote;
                LookupStatus.Text = Status(notes, (firstDefinitionMs is { } first ? $"首条释义 {first / 1000.0:0.0} 秒 · " : "") + status);
            }
            finally { finished = true; }
        }
        catch (OperationCanceledException)
        { QueryProgress.Update(request, initialProgress.Interrupted("查询已中断", "本次查询已结束，可重新查询")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!token.IsCancellationRequested)
            {
                LookupStatus.Text = "读取失败，请检查词库文件是否完整。";
                QueryProgress.Update(request, initialProgress.Interrupted("查询未完成", "读取失败 · 本次查询已结束，可重试"));
            }
        }
    }
    private static string Status(List<string> notes, string fallback) => string.Join(" · ", new[] { fallback }.Concat(notes));
    private async Task TryCacheAsync<T>(string kind, string query, T value)
    { try { await cache.WriteAsync(kind, query, value); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Cache failure never discards a valid result. */ } }
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
        content.Children.Add(header);
        parent.Children.Add(new Border { Background = accent ? Theme.Brush("PanelSurface") : Brushes.Transparent, CornerRadius = new(accent ? 8 : 0), BorderBrush = Theme.Brush("Stroke"), BorderThickness = accent ? new(1) : new(0,0,0,1), Padding = accent ? new(16,14,16,12) : new(0,12,0,14), Margin = new(0,0,0,12), Child = content });
        return content;
    }
    private void RenderLocal(LocalHit[] hits, string query)
    {
        LocalPanel.Children.Clear();
        if (!settings.OfflineEnabled) { LocalPanel.Children.Add(Hint("离线词典已停用，可在设置中重新启用。")); return; }
        if (hits.Length == 0) { LocalPanel.Children.Add(Hint("已启用的离线词典未收录此词。")); return; }
        Phonetic.Text = hits.Select(h => h.Entry.Phonetic).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? "";
        foreach (var hit in hits)
        {
            var entry = hit.Entry;
            var card = Card(LocalPanel, hit.Dictionary.Name, "离线原文");
            if (Queries.Normalize(entry.Word) != query) card.Children.Add(Hint("词条：" + entry.Word));
            if (!string.IsNullOrWhiteSpace(entry.Chinese)) card.Children.Add(Selectable(entry.Chinese));
            if (!string.IsNullOrWhiteSpace(entry.English)) card.Children.Add(Selectable(entry.English));
            if (!string.IsNullOrWhiteSpace(entry.Original)) card.Children.Add(new Expander { Header = "完整原始条目", Content = Selectable(entry.Original, 16), Margin = new(0,10,0,4) });
            var source = new Button { Content = "词典来源 ↗", FontSize = 14, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Left, Padding = new(0,5,0,0) };
            source.Click += (_, _) => OpenSource(hit.Dictionary.SourceUrl); card.Children.Add(source);
            card.Children.Add(Hint(hit.Dictionary.Attribution + " · " + hit.Dictionary.License));
        }
    }
    private void RenderOnline(OnlineEntry entry, bool cached)
    {
        OnlinePanel.Children.Clear();
        var stack = new StackPanel { Margin = new(0,10,0,0) };
        for (int i = 0; i < entry.Senses.Length; i++)
        {
            stack.Children.Add(new TextBlock { Text = $"[{i+1}]  {entry.Senses[i].PartOfSpeech.ToLowerInvariant()}", Foreground = accentBrush, FontSize = 15, Margin = new(0,7,0,3) });
            stack.Children.Add(Selectable(entry.Senses[i].Definition));
            foreach (var example in entry.Senses[i].Examples) { var text = Selectable(example,16); text.Foreground = muted; stack.Children.Add(text); }
        }
        var source = new Button { Content = "查看 Wiktionary 来源 ↗", HorizontalAlignment = HorizontalAlignment.Left, Foreground = accentBrush, Padding = new(0,8,0,8) };
        source.Click += (_, _) => OpenSource(entry.Url);
        stack.Children.Add(source);
        stack.Children.Add(Hint($"{(cached ? "缓存于" : "查询于")} {entry.FetchedAt.ToLocalTime():yyyy-MM-dd HH:mm} · CC BY-SA 4.0"));
        OnlinePanel.Children.Add(new Expander { Header = $"Wiktionary 原文 · {entry.Senses.Length} 条释义", Content = stack, IsExpanded = AiPanel.Children.Count == 0, Foreground = Foreground, Margin = new(2,5,2,10) });
    }
    private static void OpenSource(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { }
    }
}
