using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Lexi;

internal static class Theme
{
    public static Brush Brush(string name) => (Brush)Application.Current.FindResource(name);
    public static void ApplyChrome(Window window)
    {
        window.WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0, ResizeBorderThickness = new(6), GlassFrameThickness = new(0),
            CornerRadius = new(12), UseAeroCaptionButtons = false
        });
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            int dark = 1, rounded = 2;
            // Unsupported DWM attributes return a failure code; the opaque WPF surface remains usable.
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
            DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
        };
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
}
