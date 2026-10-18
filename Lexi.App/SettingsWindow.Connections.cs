using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Lexi.Core;

namespace Lexi;

public sealed partial class SettingsWindow
{
    private readonly CancellationTokenSource connectionLifetime = new();
    private TabItem AddConnectionsTab(TabControl tabs, TextBox codexPath)
    {
        var panel = new StackPanel { Margin = new(2,4,8,10) };
        var tab = new TabItem { Header = "账号连接", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel } };
        tabs.Items.Add(tab);
        panel.Children.Add(Text("OAuth 订阅", 22));
        panel.Children.Add(Text("在官方登录页完成 OAuth 授权，LeXi 使用同一台电脑上的登录状态。主要模型、第二模型和义项判断共用对应账号。", 14, Muted));
        panel.Children.Add(Text("登录会即时生效，并与官方客户端共用账号；取消设置不会撤销登录。", 13, Muted));
        var refreshers = new List<Func<Task>>();
        foreach (var provider in new[] { "codex", "claude" })
        {
            var card = new StackPanel();
            card.Children.Add(Text(provider == "codex" ? "ChatGPT · Codex" : "Claude", 18));
            var status = Text("等待检查登录状态", 14, Muted); AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite); card.Children.Add(status);
            var buttons = new WrapPanel();
            var login = ConnectionButton("登录 / 重新连接");
            AutomationProperties.SetName(login, provider == "codex" ? "登录 ChatGPT 订阅" : "登录 Claude 订阅");
            var refresh = ConnectionButton("检查状态");
            var signingIn = false;
            var install = ConnectionButton("安装说明 ↗");
            buttons.Children.Add(login); buttons.Children.Add(refresh); buttons.Children.Add(install); card.Children.Add(buttons);
            if (provider == "codex")
            {
                card.Children.Add(Text("Codex 程序路径（留空自动检测）", 13, Muted));
                codexPath.FontSize = 14; card.Children.Add(codexPath);
                var browse = ConnectionButton("选择程序…"); browse.HorizontalAlignment = HorizontalAlignment.Left;
                browse.Click += (_, _) => { var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "可执行程序 (*.exe)|*.exe", Title = "选择 Codex 程序" }; if (dialog.ShowDialog(this) == true) codexPath.Text = dialog.FileName; };
                card.Children.Add(browse);
            }
            async Task Refresh()
            {
                refresh.IsEnabled = false; login.IsEnabled = false;
                status.Text = "正在检查…";
                try
                {
                    var state = await ProviderConnection.CheckAsync(provider, codexPath.Text.Trim().Trim('"'), connectionLifetime.Token);
                    if (!connectionLifetime.IsCancellationRequested) { status.Text = state.Message; login.IsEnabled = state.Installed && !signingIn; }
                }
                catch (OperationCanceledException) { }
                finally { if (!connectionLifetime.IsCancellationRequested) refresh.IsEnabled = true; }
            }
            refreshers.Add(Refresh);
            refresh.Click += async (_, _) => await Refresh();
            install.Click += (_, _) => Open(provider == "codex" ? "https://learn.chatgpt.com/docs/cli" : "https://code.claude.com/docs/en/setup");
            login.Click += async (_, _) =>
            {
                signingIn = true; login.IsEnabled = false; refresh.IsEnabled = false;
                try
                {
                    var info = ProviderConnection.Command(provider, codexPath.Text.Trim().Trim('"'), true);
                    if (info == null) { status.Text = "请先安装官方客户端，或检查程序路径"; return; }
                    // The user explicitly starts this interactive login. Its terminal and browser
                    // belong to the official client; LeXi never intercepts tokens or one-time codes.
                    using var process = Process.Start(info);
                    status.Text = "已打开官方登录，请在浏览器或登录窗口中完成授权";
                    if (process != null) await process.WaitForExitAsync(connectionLifetime.Token);
                    if (!connectionLifetime.IsCancellationRequested) await Refresh();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
                { status.Text = "无法打开官方登录，请检查客户端安装或程序路径"; }
                finally { signingIn = false; if (!connectionLifetime.IsCancellationRequested) { login.IsEnabled = true; refresh.IsEnabled = true; } }
            };
            panel.Children.Add(new Border { Child = card, Background = Theme.Brush("PanelSurface"), BorderBrush = Theme.Brush("Stroke"), BorderThickness = new(1), CornerRadius = new(8), Padding = new(14), Margin = new(0,6,0,10) });
        }
        Loaded += async (_, _) => await Task.WhenAll(refreshers.Select(refresh => refresh()));
        Closed += (_, _) => connectionLifetime.Cancel();
        return tab;
    }
    private static Button ConnectionButton(string text) => new() { Content = text, Background = Theme.Brush("Surface"), BorderBrush = Theme.Brush("Stroke"), BorderThickness = new(1), FontSize = 14, Padding = new(10,6,10,6), Margin = new(0,0,8,10) };
}
