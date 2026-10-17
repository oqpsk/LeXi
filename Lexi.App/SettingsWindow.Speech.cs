using System.Windows;
using System.Windows.Controls;
using Lexi.Core;

namespace Lexi;

public sealed partial class SettingsWindow
{
    private Func<bool> AddSpeechTab(TabControl tabs, AppSettings settings)
    {
        var panel = new StackPanel { Margin = new(2,4,8,10) };
        var tab = new TabItem { Header = "发音", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel } };
        tabs.Items.Add(tab);
        panel.Children.Add(Text("发声模型", 18));
        var model = new ComboBox { ItemsSource = SpeechCatalog.Models, Margin = new(0,0,0,16) };
        panel.Children.Add(model); panel.Children.Add(Text("声线"));
        var voice = new ComboBox { Margin = new(0,0,0,14) }; panel.Children.Add(voice);
        var info = Text("", 14, Muted); panel.Children.Add(info);
        var secrets = new SpeechKeys(App.UserRoot);
        var fields = new Dictionary<string, (PasswordBox Input, CheckBox Remove)>();
        var credentials = new StackPanel();
        foreach (var provider in new[] { "openai", "xai" })
        {
            credentials.Children.Add(Text(provider == "openai" ? "OpenAI API Key" : "Grok / xAI API Key", 14));
            var input = new PasswordBox { Background = Theme.Brush("Surface"), Foreground = Theme.Brush("Ink"),
                BorderBrush = Theme.Brush("Stroke"), Padding = new(10,8,10,8), Margin = new(0,0,0,7),
                ToolTip = "留空保留已有密钥；新密钥只在本机加密保存。" };
            credentials.Children.Add(input);
            credentials.Children.Add(Text(secrets.HasSaved(provider) ? "已在本机保存密钥，留空保留。" : "尚未保存密钥，也可使用对应的 API Key 环境变量。", 13, Muted));
            var remove = new CheckBox { Content = "清除已保存的密钥", Margin = new(0,0,0,18) }; credentials.Children.Add(remove);
            fields.Add(provider, (input, remove));
        }
        panel.Children.Add(new Expander { Header = "在线语音连接", Content = credentials, Margin = new(0,3,0,12) });
        panel.Children.Add(Text("OpenAI / Grok 语音使用 API Key 和独立 API 额度，现有查词订阅登录仅用于查词。Windows 发音无需联网。这里播放的是合成语音。", 14, Muted));
        var testRow = new StackPanel { Orientation = Orientation.Horizontal };
        var sample = new TextBox { Text = "Hello, this is LeXi.", Width = 260, FontSize = 15, Margin = new(0,0,10,0) };
        var preview = new Button { Content = "试听", Padding = new(16,6,16,6) }; testRow.Children.Add(sample); testRow.Children.Add(preview); panel.Children.Add(testRow);
        var status = Text("", 14, Muted); status.Margin = new(0,10,0,0); panel.Children.Add(status);
        var playback = new SpeechPlayback(App.UserRoot); bool playing = false; int generation = 0;
        Closed += (_, _) => { generation++; playback.Dispose(); foreach (var field in fields.Values) field.Input.Clear(); };
        void Choices()
        {
            if (model.SelectedItem is not SpeechModel selected) return;
            generation++; playing = false; playback.Stop(); preview.Content = "试听";
            var available = selected.Provider == "windows" ? LocalSpeech.Voices() : SpeechCatalog.Voices(selected.Provider, selected.Id);
            if (available.Length == 0) available = [new("auto", "本机没有可用声线")];
            voice.ItemsSource = available;
            voice.SelectedItem = available.FirstOrDefault(v => selected.Provider == settings.SpeechProvider && selected.Id == settings.SpeechModel && v.Id == settings.SpeechVoice) ?? available.FirstOrDefault();
            info.Text = selected.Provider switch
            {
                "windows" => "使用本机已安装的 Windows 声线，首次生成后缓存。",
                "xai" => "Grok TTS 的模型由服务管理，可选择声线。",
                _ => "使用 OpenAI 官方语音接口，声线随模型切换。"
            };
        }
        model.SelectionChanged += (_, _) => Choices();
        model.SelectedItem = ((SpeechModel[])model.ItemsSource).FirstOrDefault(m => m.Provider == settings.SpeechProvider && m.Id == settings.SpeechModel) ?? ((SpeechModel[])model.ItemsSource)[0];
        preview.Click += async (_, _) =>
        {
            if (playing) { generation++; playback.Stop(); playing = false; preview.Content = "试听"; status.Text = "已停止"; return; }
            if (model.SelectedItem is not SpeechModel selected || voice.SelectedItem is not SpeechVoice chosen) return;
            var request = ++generation; playing = true; preview.Content = "停止";
            try
            {
                var key = selected.Provider == "windows" || string.IsNullOrWhiteSpace(fields[selected.Provider].Input.Password) ? null : fields[selected.Provider].Input.Password;
                await playback.PlayAsync(sample.Text, new(selected.Provider, selected.Id, chosen.Id), settings.Online,
                    text => { if (request == generation) status.Text = text; }, key);
                if (request == generation) status.Text = "试听结束";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or TimeoutException or System.ComponentModel.Win32Exception)
            { if (request == generation) status.Text = ex.Message; }
            finally { if (request == generation) { playing = false; preview.Content = "试听"; } }
        };
        return () =>
        {
            try
            {
                if (model.SelectedItem is not SpeechModel selected || voice.SelectedItem is not SpeechVoice chosen)
                    throw new InvalidOperationException("请选择可用的发声模型和声线。");
                foreach (var (provider, field) in fields)
                    if (field.Remove.IsChecked == true) secrets.Save(provider, "");
                    else if (!string.IsNullOrWhiteSpace(field.Input.Password)) secrets.Save(provider, field.Input.Password);
                settings.SpeechProvider = selected.Provider; settings.SpeechModel = selected.Id; settings.SpeechVoice = chosen.Id;
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { tabs.SelectedItem = tab; status.Text = ex.Message; return false; }
        };
    }
}
