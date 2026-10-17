using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Lexi.Core;

namespace Lexi;

public sealed partial class SettingsWindow
{
    private Func<bool> AddSpeechTab(TabControl tabs, AppSettings settings, SpeechClient? speechMetadata)
    {
        var panel = new StackPanel { Margin = new(2,4,8,10) };
        var tab = new TabItem { Header = "发音", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel } };
        tabs.Items.Add(tab);
        panel.Children.Add(Text("发声模型", 18));
        var model = new ComboBox { ItemsSource = SpeechCatalog.Models, Margin = new(0,0,0,16) };
        panel.Children.Add(model); panel.Children.Add(Text("声线"));
        var voice = new SpeechVoicePicker { Style = (Style)FindResource(typeof(ComboBox)), Margin = new(0,0,0,8) }; panel.Children.Add(voice);
        var voiceStatus = Text("", 13, Muted); panel.Children.Add(voiceStatus);
        AutomationProperties.SetName(voiceStatus, "声线连接状态");
        AutomationProperties.SetLiveSetting(voiceStatus, AutomationLiveSetting.Polite);
        var eleven = new StackPanel { Visibility = Visibility.Collapsed };
        var buttons = new WrapPanel();
        var load = ConnectionButton("刷新声线列表");
        AutomationProperties.SetName(load, "刷新 ElevenLabs 可用声线列表");
        var apiPage = ConnectionButton("获取 API Key ↗");
        apiPage.Click += (_, _) => Open("https://elevenlabs.io/app/developers/api-keys");
        buttons.Children.Add(load); buttons.Children.Add(apiPage); eleven.Children.Add(buttons);
        var manual = new TextBox { FontSize = 14, ToolTip = "可在 ElevenLabs 声线页面复制 Voice ID。" };
        AutomationProperties.SetName(manual, "手动指定 ElevenLabs Voice ID");
        eleven.Children.Add(new Expander { Header = "指定其他声线", Content = manual, Margin = new(0,0,0,12) });
        panel.Children.Add(eleven);
        var info = Text("", 14, Muted); panel.Children.Add(info);
        var secrets = new SpeechKeys(App.UserRoot);
        var fields = new Dictionary<string, (PasswordBox Input, CheckBox Remove)>();
        var credentialPanels = new Dictionary<string, StackPanel>();
        var credentials = new StackPanel();
        foreach (var provider in new[] { "openai", "xai", "elevenlabs" })
        {
            var group = new StackPanel(); credentialPanels.Add(provider, group); credentials.Children.Add(group);
            group.Children.Add(Text(provider switch { "openai" => "OpenAI API Key", "xai" => "Grok / xAI API Key", _ => "ElevenLabs API Key" }, 14));
            var input = new PasswordBox { Background = Theme.Brush("Surface"), Foreground = Theme.Brush("Ink"),
                BorderBrush = Theme.Brush("Stroke"), Padding = new(10,8,10,8), Margin = new(0,0,0,7),
                ToolTip = "留空保留已有密钥；新密钥只在本机加密保存。" };
            group.Children.Add(input);
            if (provider == "elevenlabs") group.Children.Add(Text("粘贴创建时复制的完整 Secret Key；API Key ID 无法用于连接。", 13, Muted));
            group.Children.Add(Text(secrets.HasSaved(provider) ? "已在本机保存密钥，留空保留。" : "尚未保存密钥，也可使用对应的 API Key 环境变量。", 13, Muted));
            var remove = new CheckBox { Content = "清除已保存的密钥", Margin = new(0,0,0,18) }; group.Children.Add(remove);
            fields.Add(provider, (input, remove));
        }
        var online = new Expander { Header = "在线语音连接", Content = credentials, Margin = new(0,3,0,12) }; panel.Children.Add(online);
        panel.Children.Add(Text("发音仅在点击时生成，重复播放使用缓存。密钥在保存设置后留存于本机。这里播放的是合成语音。", 14, Muted));
        var testRow = new StackPanel { Orientation = Orientation.Horizontal };
        var sample = new TextBox { Text = "Hello, this is LeXi.", Width = 260, FontSize = 15, Margin = new(0,0,10,0) };
        var preview = new Button { Content = "试听", Padding = new(16,6,16,6) }; testRow.Children.Add(sample); testRow.Children.Add(preview); panel.Children.Add(testRow);
        var status = Text("", 14, Muted); status.Margin = new(0,10,0,0); panel.Children.Add(status);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var playback = new SpeechPlayback(App.UserRoot);
        var metadata = speechMetadata ?? new SpeechClient();
        var accountVoices = new SpeechAudio(App.UserRoot, metadata);
        CancellationTokenSource? voiceRequest = null;
        bool playing = false, closed = false, updating = false;
        int generation = 0, voiceGeneration = 0;
        string previousProvider = "";
        SpeechVoice[]? elevenVoices = null;
        bool loadingVoices = false;
        SpeechVoice? lastEleven = settings.SpeechProvider == "elevenlabs" && SpeechCatalog.ValidElevenVoice(settings.SpeechVoice)
            ? new(settings.SpeechVoice, string.IsNullOrWhiteSpace(settings.SpeechVoiceName) ? "已保存的声线" : settings.SpeechVoiceName) : null;
        string? SuppliedKey(string provider) => fields[provider].Remove.IsChecked == true ? "" :
            !string.IsNullOrWhiteSpace(fields[provider].Input.Password) ? fields[provider].Input.Password : null;
        void Choices()
        {
            if (model.SelectedItem is not SpeechModel selected) return;
            if (previousProvider == "elevenlabs") lastEleven = SpeechVoiceChoices.Pick("elevenlabs", voice.SelectedItem as SpeechVoice, "") ?? lastEleven;
            if (selected.Provider != previousProvider)
            { voiceGeneration++; voiceRequest?.Cancel(); loadingVoices = false; load.IsEnabled = true; }
            generation++; playing = false; playback.Stop(); preview.Content = "试听";
            var available = selected.Provider == "windows" ? LocalSpeech.Voices() : selected.Provider == "elevenlabs"
                ? SpeechVoiceChoices.Eleven(elevenVoices, lastEleven) : SpeechCatalog.Voices(selected.Provider, selected.Id);
            if (available.Length == 0 && selected.Provider == "windows") available = [new("auto", "本机没有可用声线")];
            var preferred = selected.Provider == "elevenlabs" ? lastEleven?.Id :
                selected.Provider == settings.SpeechProvider && selected.Id == settings.SpeechModel ? settings.SpeechVoice : null;
            updating = true;
            try
            {
                voice.ItemsSource = available;
                voice.SelectedItem = available.FirstOrDefault(v => v.Id == preferred) ?? available.FirstOrDefault();
                if (previousProvider != selected.Provider) manual.Clear();
            }
            finally { updating = false; }
            previousProvider = selected.Provider;
            eleven.Visibility = selected.Provider == "elevenlabs" ? Visibility.Visible : Visibility.Collapsed;
            online.Visibility = selected.Provider == "windows" ? Visibility.Collapsed : Visibility.Visible;
            foreach (var (provider, group) in credentialPanels) group.Visibility = provider == selected.Provider ? Visibility.Visible : Visibility.Collapsed;
            if (selected.Provider == "elevenlabs") online.IsExpanded = true;
            voiceStatus.Visibility = selected.Provider == "elevenlabs" ? Visibility.Visible : Visibility.Collapsed;
            if (selected.Provider == "elevenlabs") voiceStatus.Text = elevenVoices == null
                ? "展开列表即可读取账号可用声线；首次使用请先填好 API Key。"
                : elevenVoices.Length > 0 ? $"已读取 {elevenVoices.Length} 条可用声线。" : "账号未返回可用声线，可手动填写 Voice ID。";
            info.Text = selected.Provider switch
            {
                "windows" => "使用本机已安装的 Windows 声线，可离线生成；当前采用 SAPI 本地发音。",
                "xai" => "Grok TTS 的模型由服务管理，可选择声线；使用单独的 API Key 与 API 额度。",
                "elevenlabs" => "使用 ElevenLabs 账号额度。展开声线列表可读取预置及已添加的声线，刷新列表不会生成音频。ElevenReader 套餐与此分开。",
                _ => "使用 OpenAI 官方语音接口，声线随模型切换；API 额度与查词订阅分开。"
            };
        }
        voice.SelectionChanged += (_, _) => { if (!updating && voice.SelectedItem != null) manual.Clear(); };
        void InvalidateVoices()
        {
            if (closed) return;
            voiceGeneration++; voiceRequest?.Cancel(); loadingVoices = false; load.IsEnabled = true; elevenVoices = null;
            if (model.SelectedItem is SpeechModel { Provider: "elevenlabs" })
            { Choices(); voiceStatus.Text = "连接信息已更改，展开列表可重新读取声线。"; }
        }
        fields["elevenlabs"].Input.PasswordChanged += (_, _) => InvalidateVoices();
        fields["elevenlabs"].Remove.Checked += (_, _) => InvalidateVoices();
        fields["elevenlabs"].Remove.Unchecked += (_, _) => InvalidateVoices();
        model.SelectionChanged += (_, _) => Choices();
        model.SelectedItem = ((SpeechModel[])model.ItemsSource).FirstOrDefault(m => m.Provider == settings.SpeechProvider && m.Id == settings.SpeechModel) ?? ((SpeechModel[])model.ItemsSource)[0];
        void UpdateElevenChoices()
        {
            var preferred = (voice.SelectedItem as SpeechVoice)?.Id ?? lastEleven?.Id;
            var available = SpeechVoiceChoices.Eleven(elevenVoices, lastEleven);
            updating = true;
            try
            {
                voice.ItemsSource = available;
                voice.SelectedItem = available.FirstOrDefault(v => v.Id == preferred) ?? available.FirstOrDefault();
            }
            finally { updating = false; }
        }
        async Task LoadVoicesAsync(bool refresh)
        {
            if (closed || model.SelectedItem is not SpeechModel { Provider: "elevenlabs" } || loadingVoices || (!refresh && elevenVoices != null)) return;
            voiceRequest?.Cancel(); voiceRequest?.Dispose(); voiceRequest = new();
            var token = voiceRequest.Token; var request = ++voiceGeneration;
            loadingVoices = true; load.IsEnabled = false; voiceStatus.Text = "正在读取可用声线…";
            try
            {
                var loaded = await accountVoices.ElevenVoicesAsync(settings.Online, SuppliedKey("elevenlabs") ?? secrets.Read("elevenlabs"), token);
                if (closed || request != voiceGeneration || token.IsCancellationRequested) return;
                elevenVoices = loaded; UpdateElevenChoices();
                voiceStatus.Text = loaded.Length > 0 ? $"已读取 {loaded.Length} 条可用声线，请选择后试听。" : "账号未返回可用声线，可手动填写 Voice ID。";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or HttpRequestException or TimeoutException or System.ComponentModel.Win32Exception)
            { if (!closed && request == voiceGeneration) voiceStatus.Text = ex.Message; }
            finally { if (!closed && request == voiceGeneration) { loadingVoices = false; load.IsEnabled = true; } }
        }
        voice.Discover = LoadVoicesAsync;
        load.Click += async (_, _) => await voice.DiscoverAsync(true);
        preview.Click += async (_, _) =>
        {
            if (playing) { generation++; playback.Stop(); playing = false; preview.Content = "试听"; status.Text = "已停止"; return; }
            if (model.SelectedItem is not SpeechModel selected) return;
            var request = ++generation;
            try
            {
                var chosen = SpeechVoiceChoices.Pick(selected.Provider, voice.SelectedItem as SpeechVoice, manual.Text);
                if (chosen == null) throw new InvalidOperationException("请选择可用声线。");
                playing = true; preview.Content = "停止";
                var key = selected.Provider == "windows" ? null : SuppliedKey(selected.Provider);
                await playback.PlayAsync(sample.Text, new(selected.Provider, selected.Id, chosen.Id), settings.Online,
                    text => { if (!closed && request == generation) status.Text = text; }, key);
                if (!closed && request == generation) status.Text = "试听结束";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or HttpRequestException or TimeoutException or System.ComponentModel.Win32Exception)
            { if (!closed && request == generation) status.Text = ex.Message; }
            finally { if (!closed && request == generation) { playing = false; preview.Content = "试听"; } }
        };
        Closed += (_, _) =>
        {
            closed = true; generation++; voiceGeneration++; voiceRequest?.Cancel(); voiceRequest?.Dispose();
            playback.Dispose(); metadata.Dispose(); foreach (var field in fields.Values) field.Input.Clear();
        };
        return () =>
        {
            try
            {
                if (model.SelectedItem is not SpeechModel selected) throw new InvalidOperationException("请选择可用的发声模型。");
                var chosen = SpeechVoiceChoices.Pick(selected.Provider, voice.SelectedItem as SpeechVoice, manual.Text)
                    ?? throw new InvalidOperationException("请选择可用的声线。");
                SpeechCatalog.Validate("test", new(selected.Provider, selected.Id, chosen.Id));
                foreach (var (provider, field) in fields)
                    if (field.Remove.IsChecked == true) secrets.Save(provider, "");
                    else if (!string.IsNullOrWhiteSpace(field.Input.Password)) secrets.Save(provider, field.Input.Password);
                settings.SpeechProvider = selected.Provider; settings.SpeechModel = selected.Id; settings.SpeechVoice = chosen.Id;
                settings.SpeechVoiceName = chosen.Name;
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { tabs.SelectedItem = tab; status.Text = ex.Message; return false; }
        };
    }
}
