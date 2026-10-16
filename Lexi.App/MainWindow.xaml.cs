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
    private readonly Task<OfflineDictionary> dictionaryTask;
    private readonly OnlineDictionary web = new();
    private readonly Cache cache = new(App.UserRoot);
    private readonly CodexDictionary codex = new(Path.Combine(App.Root, "assets"), App.UserRoot);
    private readonly ClaudeDictionary claude = new(Path.Combine(App.Root, "assets"), App.UserRoot);
    private readonly AppSettings settings = AppSettings.Load(App.UserRoot);
    private CancellationTokenSource? lookup;
    private bool quit;
    private bool suppressSuggestions;
    private string current = "";
    private readonly Brush muted = new SolidColorBrush(Color.FromRgb(116, 129, 123));
    private readonly Brush green = new SolidColorBrush(Color.FromRgb(34, 110, 89));
    public MainWindow()
    {
        InitializeComponent();
        UpdateOnlineButton();
        dictionaryTask = Task.Run(() => new OfflineDictionary(Path.Combine(App.Root, "data")));
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
    }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (Placeholder == null) return;
        Placeholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (suppressSuggestions || !settings.OfflineEnabled || dictionaryTask?.IsCompletedSuccessfully != true) return;
        var suggestions = dictionaryTask.Result.Suggest(SearchBox.Text);
        Suggestions.ItemsSource = suggestions;
        Suggestions.Visibility = suggestions.Length > 0 && SearchBox.IsKeyboardFocused ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await SearchAsync(SearchBox.Text); }
        if (e.Key == Key.Down && Suggestions.Visibility == Visibility.Visible && Suggestions.Items.Count > 0)
        { Suggestions.SelectedIndex = 0; Suggestions.Focus(); (Suggestions.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus(); e.Handled = true; }
    }
    private async void Suggestions_Click(object sender, MouseButtonEventArgs e)
    { if (Suggestions.SelectedItem is string text) { SetQuery(text); await SearchAsync(text); } }
    private async void Suggestions_KeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter && Suggestions.SelectedItem is string text) { e.Handled = true; SetQuery(text); await SearchAsync(text); } }
    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync(SearchBox.Text);
    private async void Example_Click(object sender, RoutedEventArgs e) { var text = (string)((Button)sender).Content; SetQuery(text); await SearchAsync(text); }
    private async void Refresh_Click(object sender, RoutedEventArgs e) { if (current.Length > 0) await SearchAsync(current, true); }
    private void SetQuery(string query)
    { suppressSuggestions = true; SearchBox.Text = query; suppressSuggestions = false; Suggestions.Visibility = Visibility.Collapsed; }
    private void UpdateOnlineButton() { OnlineToggle.Content = settings.Online ? "● 在线补充" : "○ 仅离线"; OnlineToggle.Foreground = settings.Online ? green : muted; }
    private void UpdateFooter() => Footer.Text = !settings.OfflineEnabled ? "离线词典已停用 · 可在设置中启用" :
        dictionaryTask.IsCompletedSuccessfully ? $"{dictionaryTask.Result.Count:N0} 个本地词条 · ECDICT" : "离线词库不可用";
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
        var dialog = new SettingsWindow(settings, DictionaryCatalog.Inspect(Path.Combine(App.Root, "data"))) { Owner = this };
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
        var query = Queries.Normalize(raw);
        var validation = Queries.Validate(query);
        if (validation != null) { LookupStatus.Text = validation; return; }
        lookup?.Cancel(); lookup?.Dispose(); lookup = new();
        var token = lookup.Token;
        current = query;
        SetQuery(raw.Trim());
        Welcome.Visibility = Visibility.Collapsed; EntryPanel.Visibility = Visibility.Visible;
        RefreshButton.Visibility = Visibility.Visible; RefreshButton.IsEnabled = settings.Online;
        Headword.Text = query; Phonetic.Text = "";
        LocalPanel.Children.Clear(); OnlinePanel.Children.Clear(); AiPanel.Children.Clear();
        ResultsScroll.ScrollToTop(); LookupStatus.Text = "查询本地词库…";
        var notes = new List<string>();
        LocalEntry? local = null;
        try
        {
            try
            {
                if (settings.OfflineEnabled)
                {
                    var dictionary = await dictionaryTask;
                    token.ThrowIfCancellationRequested();
                    local = dictionary.Lookup(query);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { notes.Add("本地词库不可用"); }
            token.ThrowIfCancellationRequested();
            RenderLocal(local, query);
            var online = cache.Read<OnlineEntry>("online", query);
            if (online != null) RenderOnline(online, true);
            var aiCache = cache.Read<AiEnvelope>("ai", query);
            var matchingModel = aiCache?.Model == settings.Model && aiCache.ReasoningEffort == settings.ReasoningEffort && aiCache.Provider == settings.Provider;
            if (aiCache != null && matchingModel && aiCache.EvidenceHash == CodexDictionary.EvidenceHash(online)) RenderAi(aiCache.Entry, online, true);
            if (!settings.Online) { LookupStatus.Text = "离线模式 · 使用本地词库与已缓存释义"; return; }
            var fresh = !force && online != null && DateTimeOffset.UtcNow - online.FetchedAt < TimeSpan.FromDays(1);
            if (!fresh)
            {
                LookupStatus.Text = "本地查询完成 · 正在查在线词典…";
                try
                {
                    var fetched = await web.LookupAsync(query, token);
                    token.ThrowIfCancellationRequested();
                    if (fetched != null) { online = fetched; await TryCacheAsync("online", query, online); }
                    else if (online == null) notes.Add("在线词典未收录");
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { notes.Add("在线查询超时"); }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException) { notes.Add("在线来源暂不可用"); }
            }
            token.ThrowIfCancellationRequested();
            if (online != null) RenderOnline(online, fresh || DateTimeOffset.UtcNow - online.FetchedAt > TimeSpan.FromMinutes(1));
            else { OnlinePanel.Children.Clear(); OnlinePanel.Children.Add(Hint("Wiktionary 暂无可用词条。")); }
            var hash = CodexDictionary.EvidenceHash(online);
            if (aiCache != null && aiCache.EvidenceHash != hash) AiPanel.Children.Clear();
            if (aiCache != null && matchingModel && aiCache.EvidenceHash == hash && !force && DateTimeOffset.UtcNow - aiCache.GeneratedAt < TimeSpan.FromDays(7))
            { RenderAi(aiCache.Entry, online, true); LookupStatus.Text = Status(notes, "已显示缓存释义"); return; }
            if (!settings.AutoAi && !force) { LookupStatus.Text = Status(notes, "查询完成 · 点击“刷新补充”获取双语释义"); return; }
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
                    RenderAi(partial, online, false, streaming: true);
                    LookupStatus.Text = $"释义已显示 · 正在补充例句…";
                });
                var ai = settings.Provider == "claude"
                    ? await claude.LookupAsync(query, online, settings.Model, token, progress)
                    : await codex.LookupAsync(query, online, settings, token);
                token.ThrowIfCancellationRequested();
                streamingFinished = true;
                RenderAi(ai, online, false);
                await TryCacheAsync("ai", query, new AiEnvelope(ai, hash, DateTimeOffset.UtcNow, settings.Model, settings.ReasoningEffort, settings.Provider));
                token.ThrowIfCancellationRequested();
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or InvalidDataException or JsonException or System.ComponentModel.Win32Exception)
            {
                streamingFinished = true;
                token.ThrowIfCancellationRequested();
                if (aiCache != null && matchingModel && aiCache.EvidenceHash == hash) RenderAi(aiCache.Entry, online, true);
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
    private TextBlock Hint(string text, Brush? brush = null) => new() { Text = text, Foreground = brush ?? muted, FontSize = 12, Margin = new(0,5,0,16), TextWrapping = TextWrapping.Wrap };
    private TextBox Selectable(string text, double size = 15) => new() { Text = text, Style = (Style)FindResource("Selectable"), FontSize = size, Margin = new(0,3,0,7) };
    private StackPanel Card(Panel parent, string title, string source, bool accent = false)
    {
        var content = new StackPanel();
        var header = new Grid { Margin = new(0,0,0,13) };
        header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = accent ? green : Foreground });
        var tag = new TextBlock { Text = source, FontSize = 11, Foreground = muted, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(tag,1); header.Children.Add(tag);
        content.Children.Add(header);
        parent.Children.Add(new Border { Background = Brushes.White, CornerRadius = new(12), BorderBrush = new SolidColorBrush(Color.FromRgb(226,232,223)), BorderThickness = new(1), Padding = new(20,17,20,16), Margin = new(0,0,0,14), Child = content });
        return content;
    }
    private void RenderLocal(LocalEntry? entry, string query)
    {
        LocalPanel.Children.Clear();
        if (!settings.OfflineEnabled) { LocalPanel.Children.Add(Hint("离线词典已停用，可在设置中重新启用。")); return; }
        if (entry == null) { LocalPanel.Children.Add(Hint("离线词库未收录此词。")); return; }
        Phonetic.Text = (entry.Word != query ? "词形对应 " + entry.Word + "   " : "") + (string.IsNullOrEmpty(entry.Phonetic) ? "" : "/" + entry.Phonetic.Trim('/') + "/");
        var card = Card(LocalPanel, "离线释义", "ECDICT");
        if (!string.IsNullOrWhiteSpace(entry.Chinese)) card.Children.Add(Selectable(entry.Chinese));
        if (!string.IsNullOrWhiteSpace(entry.English))
        { var en = Selectable(entry.English); en.Margin = new(0,9,0,4); card.Children.Add(en); }
        if (string.IsNullOrWhiteSpace(entry.English)) card.Children.Add(Hint("此离线词条没有英文释义，可由在线来源补充。"));
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
        stack.Children.Add(Hint($"{(cached ? "缓存于" : "查询于")} {entry.FetchedAt.ToLocalTime():yyyy-MM-dd HH:mm} · CC BY-SA 4.0 · 英语词条"));
        OnlinePanel.Children.Add(new Expander { Header = $"Wiktionary 原文 · {entry.Senses.Length} 条释义", Content = stack, IsExpanded = AiPanel.Children.Count == 0, Foreground = Foreground, Margin = new(2,5,2,10) });
    }
    private void RenderAi(AiEntry entry, OnlineEntry? online, bool cached, bool streaming = false)
    {
        AiPanel.Children.Clear();
        if (!entry.Found) { AiPanel.Children.Add(Hint(string.IsNullOrWhiteSpace(entry.Note) ? "暂时无法可靠确认这个词的含义。" : entry.Note)); return; }
        var card = Card(AiPanel, "双语释义", (settings.Provider == "claude" ? "Claude" : "Codex") + (cached ? " · 缓存" : streaming ? " · 生成中" : ""), true);
        card.Children.Add(Hint(streaming ? "AI 正在生成 · 来源依据待完整结果确认" : entry.EvidenceStatus == "source_based" ? "AI 据在线词条整理 · 中文释义与例句为生成内容" : "AI 补充 · 未获得词典来源核验", streaming || entry.EvidenceStatus == "source_based" ? muted : Brushes.DarkGoldenrod));
        foreach (var (sense, i) in entry.Senses.Select((s,i)=>(s,i)))
        {
            var label = $"{i+1:00}  {sense.PartOfSpeech}" + (string.IsNullOrWhiteSpace(sense.Register) ? "" : "  ·  " + sense.Register);
            card.Children.Add(new TextBlock { Text = label, Foreground = green, FontSize = 12, Margin = new(0,i == 0 ? 0 : 17,0,5) });
            card.Children.Add(Selectable(sense.Chinese,17));
            card.Children.Add(Selectable(sense.English));
            if (!string.IsNullOrWhiteSpace(sense.Example))
            {
                var example = new StackPanel(); var en = Selectable(sense.Example,14); en.FontStyle = FontStyles.Italic; example.Children.Add(en);
                if (!string.IsNullOrWhiteSpace(sense.ExampleChinese)) { var zh = Selectable(sense.ExampleChinese,13); zh.Foreground = muted; example.Children.Add(zh); }
                card.Children.Add(new Border { Child = example, BorderBrush = new SolidColorBrush(Color.FromRgb(197,218,205)), BorderThickness = new(2,0,0,0), Padding = new(12,2,0,0), Margin = new(0,7,0,4) });
            }
            if (sense.Evidence.Length > 0 && online != null)
            {
                var evidence = new Button { Content = "原文依据 " + string.Join(" ",sense.Evidence.Select(n=>$"[{n}]")), FontSize = 11, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Left, Padding = new(0,3,0,0) };
                evidence.Click += (_, _) => { if (OnlinePanel.Children.OfType<Expander>().FirstOrDefault() is { } expander) { expander.IsExpanded = true; expander.BringIntoView(); } };
                card.Children.Add(evidence);
            }
        }
        if (!string.IsNullOrWhiteSpace(entry.Note)) card.Children.Add(Hint(entry.Note));
        if (OnlinePanel.Children.OfType<Expander>().FirstOrDefault() is { } onlinePanel) onlinePanel.IsExpanded = false;
    }
    private static void OpenSource(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "en.wiktionary.org") return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { }
    }
}
