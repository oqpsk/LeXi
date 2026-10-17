using System.Drawing;
using Forms = System.Windows.Forms;

namespace Lexi;

// Windows-only shell integration; navigation and settings remain in Lexi.Core.
internal sealed class TrayIcon : IDisposable
{
    private readonly Icon image;
    private readonly Forms.ContextMenuStrip menu;
    private readonly Forms.NotifyIcon icon;
    private bool disposed;

    public TrayIcon(Action open, Action settings, Action exit)
    {
        image = LoadImage();
        menu = CreateMenu(open, settings, exit);
        icon = new Forms.NotifyIcon { Icon = image, Text = "LeXi · 词典与翻译", ContextMenuStrip = menu };
        icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) open(); };
        icon.Visible = true;
    }

    internal static Icon LoadImage()
    {
        using var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Lexi;component/Assets/Lexi.ico")).Stream;
        using var original = new Icon(resource, Forms.SystemInformation.SmallIconSize);
        return (Icon)original.Clone();
    }

    internal static Forms.ContextMenuStrip CreateMenu(Action open, Action settings, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开 LeXi", null, (_, _) => open());
        menu.Items.Add("设置…", null, (_, _) => settings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出 LeXi", null, (_, _) => exit());
        return menu;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        icon.Visible = false; icon.Dispose(); menu.Dispose(); image.Dispose();
    }
}
