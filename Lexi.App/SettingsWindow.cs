using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lexi.Core;

namespace Lexi;

public sealed partial class SettingsWindow : Window
{
    private static Brush Muted => Theme.Brush("Muted");
    private static Brush Accent => Theme.Brush("Accent");
    private static TextBlock Text(string text, double size = 15, Brush? color = null) => new() { Text = text, FontSize = size, Foreground = color ?? Theme.Brush("Ink"), TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,10) };
    public SettingsWindow(AppSettings settings, InstalledDictionary[] installed) : this(settings, installed, null) { }
    internal SettingsWindow(AppSettings settings, InstalledDictionary[] installed, SpeechClient? speechMetadata, CompatibleTextClient? textMetadata = null, string? textRoot = null)
    {
        Title = "LeXi 设置"; Width = 620; Height = 665; MinWidth = 540; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)Application.Current.FindResource("AppWindow");
        Theme.ApplyChrome(this);
        var root = new DockPanel { Margin = new(22,16,22,20) };
        var frame = new Border { BorderBrush = Theme.Brush("Stroke"), BorderThickness = new(1), CornerRadius = new(12), Child = root };
        frame.SetResourceReference(Border.BackgroundProperty, "WindowSurface"); Content = frame;
        var header = new DockPanel { Margin = new(0,0,0,15), Background = Brushes.Transparent };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var close = new Button { Content = Theme.Icon("CloseIcon"), Style = (Style)FindResource("IconButton"), ToolTip = "关闭设置" };
        close.Click += (_, _) => DialogResult = false; DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var title = Text("LeXi 设置", 22); title.Margin = new(0,3,0,0); header.Children.Add(title);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0,18,0,0) };
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var cancel = new Button { Content = "取消", Padding = new(22,10,22,10), Margin = new(0,0,8,0), IsCancel = true };
        cancel.Click += (_, _) => DialogResult = false; actions.Children.Add(cancel);
        var save = new Button { Content = "保存", Style = (Style)FindResource("PrimaryButton"), Background = Theme.Brush("Accent"), Foreground = Theme.Brush("Canvas"), Padding = new(26,10,26,10), IsDefault = true }; actions.Children.Add(save);
        var tabs = new TabControl();
        root.Children.Add(tabs);
        var general = new StackPanel { Margin = new(2,4,8,10) };
        tabs.Items.Add(new TabItem { Header = "查询与模型", Padding = new(18,9,18,9), Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = general } });
        var online = new CheckBox { Content = "在线补充", IsChecked = settings.Online, Margin = new(0,0,0,14) };
        var ai = new CheckBox { Content = "自动补充双语释义", IsChecked = settings.AutoAi, Margin = new(0,0,0,13) };
        general.Children.Add(online); general.Children.Add(ai);
        general.Children.Add(Text("通过 OAuth 订阅或 API 查询。两个模型独立查词，先返回的释义先显示。文本翻译使用主要模型。", 14, Muted));
        general.Children.Add(Text("查过的内容直接使用缓存。切换模型后，点击“重新生成”才会更新已有结果。", 14, Muted));
        var connections = new WrapPanel { Margin = new(0,3,0,0) };
        var accounts = ConnectionButton("OAuth 连接…"); accounts.Margin = new(0,0,8,0);
        var customEntry = ConnectionButton("API 连接…"); customEntry.Margin = new(0);
        System.Windows.Automation.AutomationProperties.SetName(accounts, "OAuth 连接");
        System.Windows.Automation.AutomationProperties.SetName(customEntry, "API 连接");
        connections.Children.Add(accounts); connections.Children.Add(customEntry); general.Children.Add(connections);
        var label = Text("主要模型 · 查词与翻译"); label.Margin = new(0,13,0,8); general.Children.Add(label);
        var draft = settings.ForModel(LookupModel.Primary(settings));
        var modelRoot = textRoot ?? App.UserRoot;
        var details = InferenceOptions.Load(modelRoot, "codex").Concat(InferenceOptions.Load(modelRoot, "claude")).Concat(InferenceOptions.Load(modelRoot, CompatibleText.Provider, settings.CompatibleBaseUrl)).ToArray();
        var choices = ModelCatalog.Choices(draft, details: details);
        var primaryChoice = LookupModel.Primary(settings); var secondChoice = LookupModel.Secondary(settings);
        ComboBox ModelSelector(LookupModel selected, IEnumerable<ModelChoice> available) => new()
        {
            ItemsSource = available, IsSynchronizedWithCurrentItem = false,
            SelectedItem = choices.First(x => x.Id == selected.Model && x.Provider == selected.Provider), Margin = new(0,0,0,12)
        };
        var model = ModelSelector(primaryChoice, choices); general.Children.Add(model);
        var primaryOptions = new InferencePicker(draft, "主要模型"); general.Children.Add(primaryOptions);
        var parallel = new CheckBox { Content = "双模型同时查词", IsChecked = settings.ParallelLookup, Margin = new(0,12,0,12) }; general.Children.Add(parallel);
        var secondaryPanel = new StackPanel(); general.Children.Add(secondaryPanel);
        secondaryPanel.Children.Add(Text("第二个查询模型"));
        var secondary = ModelSelector(secondChoice, choices); secondaryPanel.Children.Add(secondary);
        var secondaryOptions = new InferencePicker(draft, "第二个模型"); secondaryPanel.Children.Add(secondaryOptions);
        var merge = new CheckBox { Content = "自动整理同类义项（包含单模型内部）", IsChecked = settings.MergeSenses, Margin = new(0,6,0,12) }; general.Children.Add(merge);
        var judgePanel = new StackPanel(); general.Children.Add(judgePanel);
        judgePanel.Children.Add(Text("义项判断模型"));
        var judge = ModelSelector(new(settings.JudgeProvider, settings.JudgeModel), ModelCatalog.JudgeChoices(choices)); judgePanel.Children.Add(judge);
        var judgeOptions = new InferencePicker(draft, "义项判断模型"); judgePanel.Children.Add(judgeOptions);
        void RefreshOptions()
        {
            primaryOptions.Refresh(model.SelectedItem as ModelChoice); secondaryOptions.Refresh(secondary.SelectedItem as ModelChoice); judgeOptions.Refresh(judge.SelectedItem as ModelChoice);
        }
        model.SelectionChanged += (_, _) => RefreshOptions(); secondary.SelectionChanged += (_, _) => RefreshOptions(); judge.SelectionChanged += (_, _) => RefreshOptions();
        primaryOptions.Changed += RefreshOptions; secondaryOptions.Changed += RefreshOptions; judgeOptions.Changed += RefreshOptions; RefreshOptions();
        general.Children.Add(Text("同一词义的细分用法可归为一组，优先展示涵盖更完整的原文；其他表述保留在“用法细分”中。不同含义和分歧仍分开。", 14, Muted));
        void UpdateParallelControls() { secondaryPanel.IsEnabled = parallel.IsChecked == true; judgePanel.IsEnabled = merge.IsChecked == true; }
        parallel.Checked += (_, _) => UpdateParallelControls(); parallel.Unchecked += (_, _) => UpdateParallelControls();
        merge.Checked += (_, _) => UpdateParallelControls(); merge.Unchecked += (_, _) => UpdateParallelControls(); UpdateParallelControls();
        var modelError = Text("", 14, Theme.Brush("Warning")); modelError.Visibility = Visibility.Collapsed; general.Children.Add(modelError);
        general.Children.Add(Text("Effort 和速度分别控制思考深度与服务档位。同一模型共用所选配置；OAuth 订阅与自定义 API 分开保存。", 14, Muted));
        var path = new TextBox { Text = settings.CodexPath, FontSize = 14 };
        var connectionTab = AddConnectionsTab(tabs, path);
        void RefreshModels(string endpoint, ModelChoice[] available)
        {
            var primary = model.SelectedItem as ModelChoice; var second = secondary.SelectedItem as ModelChoice; var judgment = judge.SelectedItem as ModelChoice;
            draft.CompatibleBaseUrl = endpoint;
            details = details.Where(m => m.Provider != CompatibleText.Provider).Concat(available).ToArray();
            choices = ModelCatalog.Choices(draft, details: details);
            void Set(ComboBox picker, ModelChoice[] values, ModelChoice? selected)
            { picker.ItemsSource = values; picker.SelectedItem = values.FirstOrDefault(c => c.Id == selected?.Id && c.Provider == selected?.Provider); }
            Set(model, choices, primary); Set(secondary, choices, second); Set(judge, ModelCatalog.JudgeChoices(choices), judgment);
        }
        var subscriptions = ConnectionButton("读取 OAuth 订阅模型"); subscriptions.HorizontalAlignment = HorizontalAlignment.Left;
        var subscriptionStatus = Text("通过官方 Codex / Claude Code 客户端读取模型与可用档位。", 13, Muted);
        var connectionPanel = (StackPanel)((ScrollViewer)connectionTab.Content).Content;
        var oauthHeading = connectionPanel.Children.OfType<TextBlock>().First(t => t.Text == "OAuth 订阅");
        var oauthIndex = connectionPanel.Children.IndexOf(oauthHeading);
        connectionPanel.Children.Insert(oauthIndex + 1, subscriptions); connectionPanel.Children.Insert(oauthIndex + 2, subscriptionStatus);
        subscriptions.Click += async (_, _) =>
        {
            subscriptions.IsEnabled = false;
            try
            {
                if (online.IsChecked != true) { subscriptionStatus.Text = "请先开启在线补充，再读取订阅模型。"; return; }
                var available = await InferenceOptions.ReadSubscriptions(path.Text.Trim(), connectionLifetime.Token);
                await InferenceOptions.RememberSubscriptions(modelRoot, available, connectionLifetime.Token);
                details = details.Where(m => !available.Any(a => a.Provider == m.Provider)).Concat(available).ToArray();
                RefreshModels(draft.CompatibleBaseUrl, details.Where(m => m.Provider == CompatibleText.Provider).ToArray());
                subscriptionStatus.Text = $"已读取 {available.Length} 个 OAuth 订阅模型。";
            }
            catch (OperationCanceledException) { }
            catch (Exception e) when (e is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception or System.IO.IOException)
            { subscriptionStatus.Text = "无法读取订阅模型，请检查官方客户端版本、登录状态和网络。"; }
            finally { if (!connectionLifetime.IsCancellationRequested) subscriptions.IsEnabled = true; }
        };
        var custom = AddCompatibleConnection(connectionTab, settings, RefreshModels, () => online.IsChecked == true, textMetadata, textRoot);
        customEntry.Click += (_, _) => { tabs.SelectedItem = connectionTab; ((ScrollViewer)connectionTab.Content).ScrollToTop(); };
        accounts.Click += (_, _) =>
        {
            tabs.SelectedItem = connectionTab;
            var anchor = connectionPanel.Children.OfType<TextBlock>().First(t => t.Text == "OAuth 订阅");
            _ = Dispatcher.BeginInvoke(() => anchor.BringIntoView());
        };
        general.Children.Add(Text("Enter 查询 · Shift+Enter 换行 · Esc 收起\nCtrl+L 再查一个 · Ctrl+, 设置 · Ctrl+Q 退出\n固定到任务栏第 2 位后用 Win+2 切换", 14,Muted));

        var dictionaries = new StackPanel { Margin = new(2,4,8,10) };
        tabs.Items.Add(new TabItem { Header = "离线词典", Padding = new(18,9,18,9), Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = dictionaries } });
        dictionaries.Children.Add(Text($"已安装 {installed.Count(d => d.Available)} 部词典",22));
        dictionaries.Children.Add(Text("启用的词典用于本地查询与输入建议。", 14,Muted));
        var master = new CheckBox { Content = "启用离线查询", IsChecked = settings.OfflineEnabled, Margin = new(0,0,0,12) };
        dictionaries.Children.Add(master);
        var toggles = new List<(InstalledDictionary Dictionary, CheckBox Toggle)>();
        foreach (var dictionary in installed)
        {
        var card = new StackPanel();
        var enabled = new CheckBox { Content = dictionary.Name, IsChecked = !(settings.DisabledDictionaries ?? []).Contains(dictionary.Id) && dictionary.Available, IsEnabled = dictionary.Available, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(0,0,0,16) };
        toggles.Add((dictionary, enabled));
        card.Children.Add(enabled);
        card.Children.Add(Text(dictionary.Languages, 15));
        card.Children.Add(Text($"{dictionary.Status}   ·   {dictionary.EntryCount:N0} 个检索词条   ·   {dictionary.Bytes / 1048576.0:N1} MB", 15,Muted));
        card.Children.Add(Text($"{dictionary.Attribution}\n版本：{dictionary.Version}   ·   许可：{dictionary.License}", 14,Muted));
        card.Children.Add(Text("词库位置", 14));
        var location = new TextBox { Text = dictionary.Folder, IsReadOnly = true, BorderThickness = new(0), Background = Brushes.Transparent, TextWrapping = TextWrapping.Wrap, FontSize = 14, Padding = new(0), Foreground = Muted };
        card.Children.Add(location);
        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0,14,0,0) };
        var folder = new Button { Content = "打开词库文件夹", Foreground = Accent, Padding = new(0,6,16,6) };
        folder.Click += (_, _) => Open(dictionary.Folder); links.Children.Add(folder);
        var source = new Button { Content = "项目来源 ↗", Foreground = Accent, Padding = new(0,6,0,6) };
        source.Click += (_, _) => Open(dictionary.SourceUrl); links.Children.Add(source); card.Children.Add(links);
        dictionaries.Children.Add(new Border { Background = Theme.Brush("PanelSurface"), CornerRadius = new(8), BorderBrush = Theme.Brush("Stroke"), BorderThickness = new(1), Padding = new(16), Margin = new(0,6,0,12), Child = card });
        }
        dictionaries.Children.Add(Text("取消勾选并保存即可停用，词库文件会保留。", 14,Muted));
        dictionaries.Children.Add(Text("Wiktionary 与 AI 属于在线补充，可在“查询与模型”中配置。", 14,Muted));
        var saveSpeech = AddSpeechTab(tabs, settings, speechMetadata);
        var windowOptions = new StackPanel { Margin = new(2,4,8,10) };
        tabs.Items.Add(new TabItem { Header = "窗口", Padding = new(14,9,14,9), Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = windowOptions } });
        windowOptions.Children.Add(Text("点击窗口的 X 时", 18));
        var closeBehavior = new ComboBox { ItemsSource = new[] { "最小化 · 保持后台运行", "退出 LeXi" }, SelectedIndex = settings.MinimizeOnClose ? 0 : 1, Margin = new(0,0,0,14) };
        System.Windows.Automation.AutomationProperties.SetName(closeBehavior, "窗口关闭行为");
        windowOptions.Children.Add(closeBehavior);
        windowOptions.Children.Add(Text("最小化后保留任务栏入口，可用 Win+2 切回。右下角托盘的 ξ 图标可打开词典、进入设置或直接退出。Windows 可能将图标放在折叠菜单中。", 14, Muted));
        windowOptions.Children.Add(Text("Esc 始终收起窗口；Ctrl+Q 和托盘中的“退出 LeXi”始终结束应用。", 14, Muted));
        windowOptions.Children.Add(Text("Alt+← 后退 · Alt+→ 前进\n顶部中间的 LeXi 区域可拖动窗口。", 14, Muted));
        save.Click += (_, _) =>
        {
            if (model.SelectedItem is not ModelChoice primaryValue || secondary.SelectedItem is not ModelChoice secondaryValue || judge.SelectedItem is not ModelChoice judgeValue)
            { modelError.Text = "请先读取模型列表并选择模型。"; modelError.Visibility = Visibility.Visible; tabs.SelectedIndex = 0; return; }
            var candidate = settings.ForModel(new(primaryValue.Provider, primaryValue.Id));
            try { candidate.CompatibleBaseUrl = custom.BaseUrl(); }
            catch (CompatibleException ex) { modelError.Text = ex.Message; modelError.Visibility = Visibility.Visible; tabs.SelectedIndex = 0; return; }
            candidate.JudgeProvider = judgeValue.Provider; candidate.JudgeModel = judgeValue.Id;
            candidate.InferenceProfiles = [.. draft.InferenceProfiles];
            foreach (var selected in new[] { primaryValue }.Concat(candidate.ParallelLookup ? new[] { secondaryValue } : []).Concat(merge.IsChecked == true ? new[] { judgeValue } : []))
            {
                var options = InferenceOptions.Selected(candidate, new(selected.Provider, selected.Id));
                if (InferenceOptions.ValidateSelection(selected.Capabilities, options.Effort, options.Speed) is { } error)
                { modelError.Text = error; modelError.Visibility = Visibility.Visible; tabs.SelectedIndex = 0; return; }
            }
            candidate.ParallelLookup = parallel.IsChecked == true; candidate.SecondaryProvider = secondaryValue.Provider; candidate.SecondaryModel = secondaryValue.Id;
            if (ModelCatalog.Validate(candidate) is { } invalid)
            { modelError.Text = invalid; modelError.Visibility = Visibility.Visible; modelError.BringIntoView(); return; }
            if (!custom.Save()) return;
            if (!saveSpeech()) return;
            settings.CompatibleBaseUrl = candidate.CompatibleBaseUrl; settings.CompatibleStreaming = custom.Streaming();
            settings.JudgeProvider = judgeValue.Provider;
            settings.MinimizeOnClose = closeBehavior.SelectedIndex == 0;
            settings.Online = online.IsChecked == true; settings.AutoAi = ai.IsChecked == true;
            settings.OfflineEnabled = master.IsChecked == true;
            settings.DisabledDictionaries = toggles.Where(t => t.Toggle.IsChecked != true).Select(t => t.Dictionary.Id).ToArray();
            settings.CodexPath = path.Text.Trim().Trim('"'); settings.Model = ((ModelChoice)model.SelectedItem).Id;
            settings.Provider = ((ModelChoice)model.SelectedItem).Provider;
            settings.ParallelLookup = parallel.IsChecked == true; settings.MergeSenses = merge.IsChecked == true;
            settings.SecondaryProvider = secondaryValue.Provider; settings.SecondaryModel = secondaryValue.Id;
            settings.JudgeModel = ((ModelChoice)judge.SelectedItem).Id;
            settings.InferenceProfiles = [.. draft.InferenceProfiles]; DialogResult = true;
        };
    }
    private static void Open(string target)
    { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { } }
}
