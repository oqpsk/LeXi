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
    private readonly AppSettings settings = AppSettings.Load(App.UserRoot);
    private CancellationTokenSource? lookup;
    private bool quit;
    private bool suppressSuggestions;
    private string current = "";
    private readonly Brush muted = Theme.Brush("Muted");
    private readonly Brush green = Theme.Brush("Accent");
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
                var dictionary = await dictionaryTask;
                UpdateFooter();
                if (current.Length == 0) LookupStatus.Text = "准备就绪";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { LookupStatus.Text = "离线词库未能加载，可继续使用在线查询。"; Footer.Text = "词库文件缺失，请保留整个 Lexi 文件夹。"; }
        };
        Closing += (_, e) =>
        {
            if (!quit && !Dispatcher.HasShutdownStarted) { e.Cancel = true; WindowState = WindowState.Minimized; }
        };
        Closed += (_, _) => { lookup?.Cancel(); claude.Dispose(); web.Dispose(); if (dictionaryTask.IsCompletedSuccessfully) dictionaryTask.Result.Dispose(); };
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
    private void UpdateOnlineButton() { OnlineToggle.Content = settings.Online ? "● 在线补充" : "○ 仅离线"; OnlineToggle.Foreground = settings.Online ? green : muted; }
    private void UpdateFooter() => Footer.Text = !settings.OfflineEnabled ? "离线词典已停用 · 可在设置中启用" :
        dictionaryTask.IsCompletedSuccessfully ? $"{dictionaryTask.Result.Dictionaries.Count(d => d.Available && !(settings.DisabledDictionaries ?? []).Contains(d.Id))} 部离线词典" : "离线词库不可用";
    private async void Online_Click(object sender, RoutedEventArgs e)
    {
        settings.Online = !settings.Online; SaveSettings(); UpdateOnlineButton();
        lookup?.Cancel();
        UpdatePreparedModel();
        if (current.Length > 0) await SearchAsync(current);
    }
    private void UpdatePreparedModel()
    {
        if (settings.Online && settings.AutoAi && settings.Provider == "claude") claude.Prepare(settings.Model);
        else claude.ReleasePrepared();
    }
    private void SaveSettings()
    {
        try { settings.Save(App.UserRoot); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LookupStatus.Text = "设置暂时无法保存，请检查文件夹写入权限。"; }
    }
    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var previous = JsonSerializer.Serialize(settings, Json.Options);
        var dialog = new SettingsWindow(settings, dictionaryTask.IsCompletedSuccessfully ? dictionaryTask.Result.Dictionaries : DictionaryCatalog.InspectAll(Path.Combine(App.Root, "data"))) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            SaveSettings(); UpdateOnlineButton(); UpdateFooter();
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
        lookup?.Cancel(); lookup?.Dispose(); lookup = new();
        var token = lookup.Token;
        current = query;
        SetQuery(raw.Trim());
        Welcome.Visibility = Visibility.Collapsed; EntryPanel.Visibility = Visibility.Visible;
        RefreshButton.Visibility = Visibility.Visible; RefreshButton.IsEnabled = settings.Online;
        Headword.Text = raw.Trim(); Headword.FontSize = 28; Phonetic.Text = "";
        LocalPanel.Children.Clear(); OnlinePanel.Children.Clear(); AiPanel.Children.Clear();
        ResultsScroll.ScrollToTop(); LookupStatus.Text = "查询本地词库…";
        var notes = new List<string>();
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
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { notes.Add("本地词库不可用"); }
            token.ThrowIfCancellationRequested();
            RenderLocal(local, query);
            var online = cache.Read<OnlineEntry>("online-v2", query);
            if (online != null) RenderOnline(online, true);
            var evidence = DictionaryEvidence.Combine(query, local, online);
            var aiCache = cache.Read<AiEnvelope>("ai-v2", query);
            var matchingModel = aiCache?.Model == settings.Model && aiCache.ReasoningEffort == settings.ReasoningEffort && aiCache.Provider == settings.Provider;
            if (aiCache != null && matchingModel && aiCache.EvidenceHash == CodexDictionary.EvidenceHash(evidence)) RenderAi(aiCache.Entry, evidence, true);
            if (!settings.Online) { LookupStatus.Text = "离线模式 · 使用本地词库与已缓存释义"; return; }
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
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { notes.Add("在线查询超时"); }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException) { notes.Add("在线来源暂不可用"); }
            }
            token.ThrowIfCancellationRequested();
            if (online != null) RenderOnline(online, fresh || DateTimeOffset.UtcNow - online.FetchedAt > TimeSpan.FromMinutes(1));
            else { OnlinePanel.Children.Clear(); if (local.Length == 0) OnlinePanel.Children.Add(Hint("Wiktionary 暂无可用词条。")); }
            evidence = DictionaryEvidence.Combine(query, local, online);
            var hash = CodexDictionary.EvidenceHash(evidence);
            if (aiCache != null && aiCache.EvidenceHash != hash) AiPanel.Children.Clear();
            if (aiCache != null && matchingModel && aiCache.EvidenceHash == hash && !force && DateTimeOffset.UtcNow - aiCache.GeneratedAt < TimeSpan.FromDays(7))
            { RenderAi(aiCache.Entry, evidence, true); LookupStatus.Text = Status(notes, "已显示缓存释义"); return; }
            if (!settings.AutoAi && !force) { LookupStatus.Text = Status(notes, "查询完成 · 点击“重新生成”获取双语释义"); return; }
            var provider = settings.Provider == "claude" ? "Claude" : "Codex";
            LookupStatus.Text = $"词典结果已显示 · {provider} 正在补充双语释义…";
            if (AiPanel.Children.Count == 0) AiPanel.Children.Add(Hint($"{provider} 正在整理释义…", green));
            var watch = Stopwatch.StartNew();
            long? firstDefinitionMs = null;
            var streamingFinished = false;
            try
            {
                var progress = new Progress<AiEntry>(partial =>
                {
                    if (streamingFinished || token.IsCancellationRequested || current != query) return;
                    firstDefinitionMs ??= watch.ElapsedMilliseconds;
                    RenderAi(partial, evidence, false, streaming: true);
                    LookupStatus.Text = $"释义已显示 · 正在补充例句…";
                });
                var ai = settings.Provider == "claude"
                    ? await claude.LookupAsync(query, evidence, settings.Model, token, progress)
                    : await codex.LookupAsync(query, evidence, settings, token);
                token.ThrowIfCancellationRequested();
                streamingFinished = true;
                RenderAi(ai, evidence, false);
                if (ai.Found) await TryCacheAsync("ai-v2", query, new AiEnvelope(ai, hash, DateTimeOffset.UtcNow, settings.Model, settings.ReasoningEffort, settings.Provider));
                token.ThrowIfCancellationRequested();
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or InvalidDataException or JsonException or System.ComponentModel.Win32Exception)
            {
                streamingFinished = true;
                token.ThrowIfCancellationRequested();
                if (aiCache != null && matchingModel && aiCache.EvidenceHash == hash) RenderAi(aiCache.Entry, evidence, true);
                else { AiPanel.Children.Clear(); AiPanel.Children.Add(Hint(ex.Message)); }
                notes.Add($"{provider} 补充未完成");
            }
            LookupStatus.Text = Status(notes, firstDefinitionMs is { } first ? $"首条释义 {first / 1000.0:0.0} 秒 · 查询完成" : "查询完成");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { if (!token.IsCancellationRequested) LookupStatus.Text = "读取失败，请检查词库文件是否完整。"; }
    }
    private static string Status(List<string> notes, string fallback) => notes.Count == 0 ? fallback : string.Join(" · ", notes);
    private async Task TryCacheAsync<T>(string kind, string query, T value)
    { try { await cache.WriteAsync(kind, query, value); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Cache failure never discards a valid result. */ } }
    private TextBlock Hint(string text, Brush? brush = null) => new() { Text = text, Foreground = brush ?? muted, FontSize = 11, Margin = new(0,4,0,12), TextWrapping = TextWrapping.Wrap };
    private TextBox Selectable(string text, double size = 14) => new() { Text = text, Style = (Style)FindResource("Selectable"), FontSize = size, Margin = new(0,3,0,6) };
    private StackPanel Card(Panel parent, string title, string source, bool accent = false)
    {
        var content = new StackPanel();
        var header = new Grid { Margin = new(0,0,0,10) };
        header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 12, Foreground = accent ? green : Foreground, Margin = new(0,0,10,0) });
        var tag = new TextBlock { Text = source, FontSize = 10, Foreground = muted, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(tag,1); header.Children.Add(tag);
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
            if (!string.IsNullOrWhiteSpace(entry.Original)) card.Children.Add(new Expander { Header = "完整原始条目", Content = Selectable(entry.Original, 12), Margin = new(0,10,0,4) });
            var source = new Button { Content = "词典来源 ↗", FontSize = 11, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Left, Padding = new(0,5,0,0) };
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
            stack.Children.Add(new TextBlock { Text = $"[{i+1}]  {entry.Senses[i].PartOfSpeech.ToLowerInvariant()}", Foreground = green, FontSize = 12, Margin = new(0,7,0,3) });
            stack.Children.Add(Selectable(entry.Senses[i].Definition));
            foreach (var example in entry.Senses[i].Examples) { var text = Selectable(example,13); text.Foreground = muted; stack.Children.Add(text); }
        }
        var source = new Button { Content = "查看 Wiktionary 来源 ↗", HorizontalAlignment = HorizontalAlignment.Left, Foreground = green, Padding = new(0,8,0,8) };
        source.Click += (_, _) => OpenSource(entry.Url);
        stack.Children.Add(source);
        stack.Children.Add(Hint($"{(cached ? "缓存于" : "查询于")} {entry.FetchedAt.ToLocalTime():yyyy-MM-dd HH:mm} · CC BY-SA 4.0"));
        OnlinePanel.Children.Add(new Expander { Header = $"Wiktionary 原文 · {entry.Senses.Length} 条释义", Content = stack, IsExpanded = AiPanel.Children.Count == 0, Foreground = Foreground, Margin = new(2,5,2,10) });
    }
    private void RenderAi(AiEntry entry, OnlineEntry? online, bool cached, bool streaming = false)
    {
        AiPanel.Children.Clear();
        if (!entry.Found) { AiPanel.Children.Add(Hint(string.IsNullOrWhiteSpace(entry.Note) ? "暂时无法可靠确认这个词的含义。" : entry.Note)); return; }
        var card = Card(AiPanel, "双语释义", (settings.Provider == "claude" ? "Claude" : "Codex") + (cached ? " · 缓存" : streaming ? " · 生成中" : ""), true);
        card.Children.Add(Hint(streaming ? "生成中 · 来源待确认" : entry.EvidenceStatus == "source_based" ? "据词典来源整理 · 含 AI 生成内容" : "AI 补充 · 未经词典来源核验", streaming || entry.EvidenceStatus == "source_based" ? muted : Theme.Brush("Warning")));
        foreach (var (sense, i) in entry.Senses.Select((s,i)=>(s,i)))
        {
            var label = $"{i+1:00}  {sense.PartOfSpeech}" + (string.IsNullOrWhiteSpace(sense.Register) ? "" : "  ·  " + sense.Register);
            card.Children.Add(new TextBlock { Text = label, Foreground = muted, FontSize = 11, Margin = new(0,i == 0 ? 0 : 16,0,5) });
            card.Children.Add(Selectable(sense.Chinese,16));
            card.Children.Add(Selectable(sense.English));
            if (!string.IsNullOrWhiteSpace(sense.Example))
            {
                var example = new StackPanel(); var en = Selectable(sense.Example,13); en.Foreground = muted; example.Children.Add(en);
                if (!string.IsNullOrWhiteSpace(sense.ExampleChinese)) { var zh = Selectable(sense.ExampleChinese,12); zh.Foreground = muted; example.Children.Add(zh); }
                card.Children.Add(new Border { Child = example, BorderBrush = Theme.Brush("Stroke"), BorderThickness = new(2,0,0,0), Padding = new(10,0,0,0), Margin = new(0,8,0,4) });
            }
            if (sense.Evidence.Length > 0 && online != null)
            {
                foreach (var number in sense.Evidence.Distinct())
                {
                    var cited = online.Senses[number - 1];
                    var evidence = new Button { Content = $"[{number}] {cited.SourceName} ↗", FontSize = 11, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Left, Padding = new(0,3,0,0), ToolTip = cited.Definition };
                    evidence.Click += (_, _) => OpenSource(string.IsNullOrEmpty(cited.SourceUrl) ? online.Url : cited.SourceUrl);
                    card.Children.Add(evidence);
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(entry.Note)) card.Children.Add(Hint(entry.Note));
        if (OnlinePanel.Children.OfType<Expander>().FirstOrDefault() is { } onlinePanel) onlinePanel.IsExpanded = false;
    }
    private static void OpenSource(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { }
    }
}
