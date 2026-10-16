using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Lexi;

internal static partial class Program
{
    private static void CheckNativeFrame()
    {
        // EnsureHandle constructs an HWND but never shows or activates it. This
        // checks native configuration only; it is not a desktop interaction test.
        var window = new Window { Width = 500, Height = 400, ShowInTaskbar = false };
        Theme.ApplyChrome(window);
        try
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            if (IsWindowVisible(handle)) throw new Exception("Native check unexpectedly displayed a window.");
            var style = GetWindowLongW(handle, -16);
            if ((style & 0x00C00000) != 0 || (style & 0x00040000) == 0)
                throw new Exception("Custom chrome must omit WS_CAPTION and retain WS_THICKFRAME.");
            Console.WriteLine("PASS: non-displaying native window has no system caption and retains resizing.");
            if (Environment.OSVersion.Version < new Version(10, 0, 22621))
            {
                Console.WriteLine("SKIP: native caption/backdrop attributes require Windows 11 22H2.");
                return;
            }
            // DWMWA_CAPTION_COLOR is write-only. ApplyBackdrop permits a translucent
            // surface only if DWM accepts caption suppression and the backdrop.
            var surface = (SolidColorBrush)window.Resources["WindowSurface"];
            if (surface.Color.A < 255 && (DwmGetWindowAttribute(handle, 38, out var backdrop, sizeof(int)) != 0 || backdrop != 3))
                throw new Exception("A translucent surface requires native Desktop Acrylic.");
            Console.WriteLine(surface.Color.A < 255
                ? "PASS: Desktop Acrylic remains active behind the translucent surface."
                : "PASS: native glass is unavailable or disabled; the surface remains opaque.");
        }
        finally { window.Close(); }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")]
    private static extern int GetWindowLongW(IntPtr handle, int index);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int size);
}
