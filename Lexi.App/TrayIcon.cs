using System.Drawing;
using System.Drawing.Drawing2D;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
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
        var menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false, ShowCheckMargin = false,
            BackColor = Palette("Surface"), ForeColor = Palette("Ink"),
            Padding = new Forms.Padding(5), Font = new Font("Microsoft YaHei UI", 10),
            Renderer = new MenuRenderer(), AccessibleName = "LeXi 菜单"
        };
        var font = menu.Font;
        menu.Disposed += (_, _) => font.Dispose();
        menu.Items.Add("打开 LeXi", null, (_, _) => open());
        menu.Items.Add("设置…", null, (_, _) => settings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出 LeXi", null, (_, _) => exit());
        foreach (Forms.ToolStripItem item in menu.Items)
            if (item is Forms.ToolStripMenuItem) item.Padding = new Forms.Padding(12, 7, 18, 7);
        return menu;
    }

    private static System.Drawing.Color Palette(string key)
    {
        var color = ((SolidColorBrush)Theme.Brush(key)).Color;
        return System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
    }
    private sealed class MenuRenderer : Forms.ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Palette("Surface"));
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }
        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Palette("Stroke"));
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var bounds = new Rectangle(0, 1, e.Item.Width - 1, e.Item.Height - 2);
            using var shape = new GraphicsPath();
            const int diameter = 10;
            shape.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            shape.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            shape.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            shape.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            shape.CloseFigure();
            var previous = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Palette("Hover"));
            e.Graphics.FillPath(brush, shape);
            e.Graphics.SmoothingMode = previous;
        }
        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Palette(!e.Item.Enabled ? "Muted" : e.Item.Selected ? "Accent" : "Ink");
            base.OnRenderItemText(e);
        }
        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Palette("Stroke"));
            e.Graphics.DrawLine(pen, 12, e.Item.Height / 2, e.Item.Width - 12, e.Item.Height / 2);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        icon.Visible = false; icon.Dispose(); menu.Dispose(); image.Dispose();
    }
}
