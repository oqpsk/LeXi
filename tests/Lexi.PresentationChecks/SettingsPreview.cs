using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lexi;
using Lexi.Core;

internal static partial class Program
{
    private static void RenderSettingsPreview(string file)
    {
        var root = Path.Combine(Path.GetTempPath(), "lexi-preview-" + Guid.NewGuid().ToString("N"));
        var settings = new AppSettings { Provider = "claude", Model = "sonnet", SecondaryProvider = "codex", SecondaryModel = "gpt-6-luna" };
        var window = new SettingsWindow(settings, [], null, textRoot: root);
        try
        {
            var frame = (FrameworkElement)window.Content;
            var tabs = Contents(frame).OfType<TabControl>().Single(); tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => (string)t.Header == "查询与模型");
            Measure(frame,900,760);
            var bitmap = new RenderTargetBitmap(900,760,96,96,PixelFormats.Pbgra32); bitmap.Render(frame);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(file); png.Save(output);
        }
        finally { window.Close(); if (Directory.Exists(root)) Directory.Delete(root,true); }
    }
}
