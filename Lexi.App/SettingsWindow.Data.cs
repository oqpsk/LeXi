using System.IO;
using System.Windows;
using System.Windows.Controls;
using Lexi.Core;

namespace Lexi;

public sealed partial class SettingsWindow
{
    private static void Group(StackPanel panel, string first, UIElement start, UIElement second, string secondTitle, UIElement third, string thirdTitle)
    {
        var items = panel.Children.Cast<UIElement>().ToArray(); panel.Children.Clear();
        var groups = new[] { (0, Array.IndexOf(items, second), first), (Array.IndexOf(items, second), Array.IndexOf(items, third), secondTitle), (Array.IndexOf(items, third), items.Length, thirdTitle) };
        foreach (var (from, to, title) in groups)
        {
            var body = new StackPanel(); body.Children.Add(Text(title, 18));
            foreach (var child in items.Skip(from).Take(to - from)) body.Children.Add(child);
            panel.Children.Add(new Border { Child = body, Background = Theme.Brush("PanelSurface"), BorderBrush = Theme.Brush("Stroke"), BorderThickness = new(1), CornerRadius = new(9), Padding = new(16), Margin = new(0,0,0,14) });
        }
    }
    private void AddDataTab(TabControl tabs, AppSettings settings, string root, QuerySession? session)
    {
        var panel = new StackPanel { Margin = new(2,4,8,10) };
        tabs.Items.Add(new TabItem { Header = "数据与迁移", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel } });
        var data = new UserData(root); var history = session?.History ?? new QueryHistory(root);
        panel.Children.Add(Text("备份与迁移", 20));
        panel.Children.Add(Text("备份含设置、历史和已保存查询，可能包含私人文本。密钥、OAuth 登录、语音和词库文件不会导出；在新设备重新连接账号。", 14, Muted));
        var status = Text("", 14, Muted);
        bool Ready()
        {
            if (session?.View.Progress?.Busy == true) { status.Text = "请等待当前查询结束，再管理数据。"; return false; }
            return true;
        }
        var row = new WrapPanel(); panel.Children.Add(row);
        var export = ConnectionButton("导出备份…"); var import = ConnectionButton("导入备份…"); row.Children.Add(export); row.Children.Add(import);
        var includeSettings = new CheckBox { Content = "导入时同时恢复设置", IsChecked = false, Margin = new(0,6,0,12) }; panel.Children.Add(includeSettings);
        export.Click += async (_, _) =>
        {
            if (!Ready()) return;
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "LeXi 备份 (*.lexibackup)|*.lexibackup", FileName = "LeXi-backup-" + DateTime.Now.ToString("yyyyMMdd") + ".lexibackup" };
            if (dialog.ShowDialog(this) != true) return;
            export.IsEnabled = false;
            try { await (session != null ? session.ManageDataAsync(async () => { await Task.Run(() => data.ExportAsync(dialog.FileName, settings, history)); return true; }) : Task.Run(async () => { await data.ExportAsync(dialog.FileName, settings, history); return true; })); status.Text = "备份已导出，不含密钥。"; }
            catch (Exception e) when (DataError(e)) { status.Text = "备份无法写入，请检查目标位置。"; }
            finally { export.IsEnabled = true; }
        };
        import.Click += async (_, _) =>
        {
            if (!Ready()) return;
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "LeXi 备份 (*.lexibackup)|*.lexibackup" };
            if (dialog.ShowDialog(this) != true) return;
            import.IsEnabled = false;
            try
            {
                var preview = await Task.Run(() => data.Inspect(dialog.FileName));
                if (MessageBox.Show(this, $"将合入 {preview.History} 条历史、{preview.Results} 份查询。已有查询会保留。" + (includeSettings.IsChecked == true ? "设置会恢复；账号密钥需重新填写。" : "当前设置保持。"), "导入 LeXi 备份", MessageBoxButton.OKCancel) != MessageBoxResult.OK || !Ready()) return;
                if (session != null) await session.ManageDataAsync(() => Task.FromResult(data.Import(dialog.FileName, settings, history, includeSettings.IsChecked == true)));
                else data.Import(dialog.FileName, settings, history, includeSettings.IsChecked == true);
                DataImported = true; DialogResult = false;
            }
            catch (Exception e) when (DataError(e)) { status.Text = "备份无法导入：文件无效、版本不支持或目录不可写。"; }
            finally { import.IsEnabled = true; }
        };
        panel.Children.Add(Text("缓存", 20));
        panel.Children.Add(Text("中间缓存可重新获取；语音缓存删除后云端重复播放可能重新计费。删除已保存查询会让下次查询重新读取资料。历史和密钥均保留。", 14, Muted));
        var cacheRows = new StackPanel(); panel.Children.Add(cacheRows);
        void Refresh()
        {
            cacheRows.Children.Clear();
            try
            {
                foreach (var usage in data.Usage())
                {
                    var line = new DockPanel { Margin = new(0,0,0,10) };
                    var clear = ConnectionButton("清理…"); DockPanel.SetDock(clear, Dock.Right); line.Children.Add(clear);
                    line.Children.Add(Text($"{usage.Name}  ·  {usage.Files:N0} 个文件  ·  {usage.Bytes / 1048576.0:N1} MB")); cacheRows.Children.Add(line);
                    clear.Click += async (_, _) =>
                    {
                        if (!Ready() || MessageBox.Show(this, "清理“" + usage.Name + "”？此操作无法撤销。", "清理缓存", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
                        try { if (session != null) await session.ManageDataAsync(() => { data.Clear(usage.Id); return Task.FromResult(true); }); else data.Clear(usage.Id); status.Text = "已清理“" + usage.Name + "”。"; Refresh(); }
                        catch (Exception e) when (DataError(e)) { status.Text = "部分文件正在使用或不可写，请稍后再试。"; }
                    };
                }
            }
            catch (Exception e) when (DataError(e)) { status.Text = "缓存目录暂时无法读取。"; }
        }
        Refresh(); panel.Children.Add(status);
    }
    private static bool DataError(Exception e) => e is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException;
}
