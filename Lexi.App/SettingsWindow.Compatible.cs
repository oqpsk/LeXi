using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Lexi.Core;

namespace Lexi;

public sealed partial class SettingsWindow
{
    private sealed record CompatibleDraft(Func<string> BaseUrl, Func<bool> Streaming, Func<bool> Save);
    private CompatibleDraft AddCompatibleConnection(TabItem tab, AppSettings settings, Action<string, ModelChoice[]> models,
        Func<bool> online, CompatibleTextClient? suppliedClient, string? suppliedRoot)
    {
        var client = suppliedClient ?? new();
        var keys = new TextProviderKeys(suppliedRoot ?? App.UserRoot);
        var panel = (StackPanel)((ScrollViewer)tab.Content).Content;
        var card = new StackPanel();
        card.Children.Add(Text("自定义文本服务", 20));
        card.Children.Add(Text("OpenAI 兼容接口 · 仅用于查词、翻译和义项整理。发音在“发音”中独立设置。", 13, Muted));
        card.Children.Add(Text("Base URL", 14));
        var url = new TextBox { Text = settings.CompatibleBaseUrl, FontSize = 14, Margin = new(0,0,0,12) };
        AutomationProperties.SetName(url, "自定义文本服务 Base URL"); card.Children.Add(url);
        PasswordBox Secret(string label)
        {
            card.Children.Add(Text(label, 14));
            var input = new PasswordBox { Padding = new(8), Margin = new(0,0,0,10), FontSize = 14,
                Background = Theme.Brush("Surface"), Foreground = Theme.Brush("Ink"), BorderBrush = Theme.Brush("Stroke") };
            AutomationProperties.SetName(input, label); card.Children.Add(input); return input;
        }
        var id = Secret("CF Access Client ID"); var secret = Secret("CF Access Client Secret"); var api = Secret("API Key（可选）");
        var saved = Text("", 13, Muted); card.Children.Add(saved);
        var remove = new CheckBox { Content = "清除此地址已保存的凭据", Margin = new(0,0,0,12) }; card.Children.Add(remove);
        var stream = new CheckBox { Content = "逐片显示生成结果", IsChecked = settings.CompatibleStreaming, Margin = new(0,0,0,12) }; card.Children.Add(stream);
        var refresh = ConnectionButton("读取模型列表"); AutomationProperties.SetName(refresh, "读取自定义文本服务模型列表");
        refresh.HorizontalAlignment = HorizontalAlignment.Left; card.Children.Add(refresh);
        var status = Text("读取成功后，返回“查询与模型”选择模型。", 13, Muted);
        AutomationProperties.SetName(status, "自定义文本服务连接状态"); AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite); card.Children.Add(status);
        panel.Children.Insert(0, new Border { Child = card, Background = Theme.Brush("PanelSurface"), BorderBrush = Theme.Brush("Stroke"), BorderThickness = new(1),
            CornerRadius = new(8), Padding = new(14), Margin = new(0,0,0,18) });
        CancellationTokenSource? request = null; int generation = 0;
        string Endpoint() => CompatibleText.NormalizeBase(url.Text);
        void SavedState()
        {
            try { saved.Text = keys.HasSaved(Endpoint()) ? "凭据已为此地址加密保存；输入框留空可保留。API Key 可以留空。" : "凭据在保存设置后加密留存在本机。此入口允许 API Key 留空。"; }
            catch (CompatibleException) { saved.Text = "请填写有效的 Base URL。"; }
        }
        void Invalidate(bool endpointChanged = false)
        {
            generation++; request?.Cancel(); refresh.IsEnabled = true;
            if (endpointChanged) { id.Clear(); secret.Clear(); api.Clear(); remove.IsChecked = false; }
            models(url.Text, []); SavedState(); status.Text = "请读取模型列表，再选择要使用的模型。";
        }
        CompatibleCredentials Credentials()
        {
            CompatibleCredentials previous;
            if (remove.IsChecked == true) previous = new();
            else if (id.Password.Length > 0 && secret.Password.Length > 0)
            { try { previous = keys.Read(Endpoint()); } catch (CompatibleException) { previous = new(); } }
            else previous = keys.Read(Endpoint());
            return new(id.Password.Length == 0 ? previous.ClientId : id.Password,
                secret.Password.Length == 0 ? previous.ClientSecret : secret.Password, api.Password.Length == 0 ? previous.ApiKey : api.Password);
        }
        url.TextChanged += (_, _) => Invalidate(true);
        id.PasswordChanged += (_, _) => Invalidate(); secret.PasswordChanged += (_, _) => Invalidate(); api.PasswordChanged += (_, _) => Invalidate();
        remove.Checked += (_, _) => Invalidate(); remove.Unchecked += (_, _) => Invalidate();
        refresh.Click += async (_, _) =>
        {
            request?.Cancel(); request?.Dispose(); request = CancellationTokenSource.CreateLinkedTokenSource(connectionLifetime.Token);
            var token = request.Token; var current = ++generation; refresh.IsEnabled = false; status.Text = "正在读取模型列表…";
            try
            {
                if (!online()) throw new CompatibleException("offline", "请先开启在线补充，再读取模型列表。");
                var available = await client.ModelChoicesAsync(Endpoint(), Credentials(), token);
                if (current != generation || token.IsCancellationRequested) return;
                models(Endpoint(), available);
                var endpoint = Endpoint();
                status.Text = available.Length == 0 ? "鉴权成功，服务端暂未提供可用模型。" : $"已读取 {available.Length} 个模型。返回“查询与模型”选择主要模型、第二模型或义项判断模型。";
                await InferenceOptions.Remember(suppliedRoot ?? App.UserRoot, CompatibleText.Provider, endpoint, available, token);
            }
            catch (OperationCanceledException) { }
            catch (CompatibleException e) { if (current == generation) status.Text = e.Message; }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
            { if (current == generation) _ = Dispatcher.BeginInvoke(() => status.Text = "模型已读取，能力信息暂时无法保存到本机。请检查文件夹权限。"); }
            finally { if (current == generation && !connectionLifetime.IsCancellationRequested) _ = Dispatcher.BeginInvoke(() => refresh.IsEnabled = true); }
        };
        Closed += (_, _) => { request?.Cancel(); request?.Dispose(); if (suppliedClient == null) client.Dispose(); };
        SavedState();
        bool Save()
        {
            try
            {
                var endpoint = Endpoint();
                if (remove.IsChecked == true && id.Password.Length == 0 && secret.Password.Length == 0 && api.Password.Length == 0) keys.Remove(endpoint);
                else if (id.Password.Length > 0 || secret.Password.Length > 0 || api.Password.Length > 0) keys.Save(endpoint, Credentials());
                return true;
            }
            catch (CompatibleException e) { status.Text = e.Message; tab.IsSelected = true; status.BringIntoView(); return false; }
        }
        return new(Endpoint, () => stream.IsChecked == true, Save);
    }
}
