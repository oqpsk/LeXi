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
    private sealed record ModelChoice(string Label, string Id, string Provider = "codex") { public override string ToString() => Label; }
    public SettingsWindow(AppSettings settings, InstalledDictionary[] installed)
    {
        Title = "Lexi 设置"; Width = 620; Height = 665; MinWidth = 540; MinHeight = 480;
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
        var title = Text("Lexi 设置", 22); title.Margin = new(0,3,0,0); header.Children.Add(title);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0,18,0,0) };
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var cancel = new Button { Content = "取消", Padding = new(22,10,22,10), Margin = new(0,0,8,0), IsCancel = true };
        cancel.Click += (_, _) => DialogResult = false; actions.Children.Add(cancel);
        var save = new Button { Content = "保存", Background = Theme.Brush("Selection"), Foreground = Theme.Brush("Ink"), Padding = new(26,10,26,10), IsDefault = true }; actions.Children.Add(save);
        var tabs = new TabControl();
        root.Children.Add(tabs);
        var general = new StackPanel { Margin = new(2,4,8,10) };
        tabs.Items.Add(new TabItem { Header = "查询与模型", Padding = new(18,9,18,9), Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = general } });
        var online = new CheckBox { Content = "在线补充", IsChecked = settings.Online, Margin = new(0,0,0,14) };
        var ai = new CheckBox { Content = "自动补充双语释义", IsChecked = settings.AutoAi, Margin = new(0,0,0,13) };
        general.Children.Add(online); general.Children.Add(ai);
        general.Children.Add(Text("使用本机 Claude Code / Codex 的订阅登录。两个模型独立查词，先返回的释义先显示。文本翻译使用主要模型。", 14, Muted));
        var label = Text("主要模型 · 查词与翻译"); label.Margin = new(0,13,0,8); general.Children.Add(label);
        var choices = new List<ModelChoice> { new("Claude Haiku · 流式显示", "haiku", "claude"), new("GPT-6 Luna · Fast", "gpt-6-luna"), new("GPT-6.1 Sol · Fast", "gpt-6.1-sol"), new("Codex 默认 · Fast", "") };
        var primaryChoice = LookupModel.Primary(settings); var secondChoice = LookupModel.Secondary(settings);
        foreach (var selected in new[] { primaryChoice, secondChoice, new LookupModel("codex", settings.JudgeModel) })
            if (!choices.Any(x => x.Id == selected.Model && x.Provider == selected.Provider)) choices.Add(new(selected.Name, selected.Model, selected.Provider));
        ComboBox ModelSelector(LookupModel selected, IEnumerable<ModelChoice> available) => new()
        {
            ItemsSource = available, IsSynchronizedWithCurrentItem = false,
            SelectedItem = choices.First(x => x.Id == selected.Model && x.Provider == selected.Provider), Margin = new(0,0,0,12)
        };
        var model = ModelSelector(primaryChoice, choices); general.Children.Add(model);
        var parallel = new CheckBox { Content = "双模型同时查词", IsChecked = settings.ParallelLookup, Margin = new(0,12,0,12) }; general.Children.Add(parallel);
        var secondaryPanel = new StackPanel(); general.Children.Add(secondaryPanel);
        secondaryPanel.Children.Add(Text("第二个查询模型"));
        var secondary = ModelSelector(secondChoice, choices); secondaryPanel.Children.Add(secondary);
        var merge = new CheckBox { Content = "自动整理同类义项（包含单模型内部）", IsChecked = settings.MergeSenses, Margin = new(0,6,0,12) }; general.Children.Add(merge);
        var judgePanel = new StackPanel(); general.Children.Add(judgePanel);
        judgePanel.Children.Add(Text("义项判断模型 · 使用 Codex 订阅"));
        var judge = ModelSelector(new("codex", settings.JudgeModel), choices.Where(c => c.Provider == "codex").ToArray()); judgePanel.Children.Add(judge);
        general.Children.Add(Text("同一词义的细分用法可归为一组，优先展示涵盖更完整的原文；其他表述保留在“用法细分”中。不同含义和分歧仍分开。", 14, Muted));
        void UpdateParallelControls() { secondaryPanel.IsEnabled = parallel.IsChecked == true; judgePanel.IsEnabled = merge.IsChecked == true; }
        parallel.Checked += (_, _) => UpdateParallelControls(); parallel.Unchecked += (_, _) => UpdateParallelControls();
        merge.Checked += (_, _) => UpdateParallelControls(); merge.Unchecked += (_, _) => UpdateParallelControls(); UpdateParallelControls();
        var modelError = Text("", 14, Theme.Brush("Warning")); modelError.Visibility = Visibility.Collapsed; general.Children.Add(modelError);
        general.Children.Add(Text("Claude 关闭额外思考，释义生成后立即显示。Codex 使用 Low + Fast，优先速度并消耗更多订阅额度。", 14, Muted));
        general.Children.Add(Text(ClaudeDictionary.FindExecutable() == null ? "未检测到 Claude Code" : "已检测到 Claude Code · 登录由官方程序管理", 14,Muted));
        var advanced = new StackPanel { Margin = new(0,12,0,0) };
        advanced.Children.Add(Text("Codex 程序路径（留空自动检测）", 14));
        var path = new TextBox { Text = settings.CodexPath, FontSize = 14 }; advanced.Children.Add(path);
        advanced.Children.Add(Text(CodexDictionary.FindExecutable(settings.CodexPath) == null ? "未检测到 Codex" : "已检测到 Codex", 14,Muted));
        general.Children.Add(new Expander { Header = "连接设置", Content = advanced, Margin = new(0,15,0,15) });
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
        var saveSpeech = AddSpeechTab(tabs, settings);
        save.Click += (_, _) =>
        {
            var primaryValue = (ModelChoice)model.SelectedItem; var secondaryValue = (ModelChoice)secondary.SelectedItem;
            if (parallel.IsChecked == true && primaryValue.Provider == secondaryValue.Provider && primaryValue.Id == secondaryValue.Id)
            { modelError.Text = "请选择两个不同的查询模型。"; modelError.Visibility = Visibility.Visible; modelError.BringIntoView(); return; }
            if (!saveSpeech()) return;
            settings.Online = online.IsChecked == true; settings.AutoAi = ai.IsChecked == true;
            settings.OfflineEnabled = master.IsChecked == true;
            settings.DisabledDictionaries = toggles.Where(t => t.Toggle.IsChecked != true).Select(t => t.Dictionary.Id).ToArray();
            settings.CodexPath = path.Text.Trim().Trim('"'); settings.Model = ((ModelChoice)model.SelectedItem).Id;
            settings.Provider = ((ModelChoice)model.SelectedItem).Provider;
            settings.ParallelLookup = parallel.IsChecked == true; settings.MergeSenses = merge.IsChecked == true;
            settings.SecondaryProvider = secondaryValue.Provider; settings.SecondaryModel = secondaryValue.Id;
            settings.JudgeModel = ((ModelChoice)judge.SelectedItem).Id;
            settings.ReasoningEffort = "low"; settings.ServiceTier = "fast"; DialogResult = true;
        };
    }
    private static void Open(string target)
    { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { } }
}
