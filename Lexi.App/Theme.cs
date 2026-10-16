using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shell;
using Microsoft.Win32;

namespace Lexi;

internal static class Theme
{
    public static Brush Brush(string name) => (Brush)Application.Current.FindResource(name);
    public static System.Windows.Shapes.Path Icon(string name) => new()
    { Data = (Geometry)Application.Current.FindResource(name), Style = (Style)Application.Current.FindResource("IconPath") };
    public static void ApplyChrome(Window window)
    {
        window.WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0, ResizeBorderThickness = new(6), GlassFrameThickness = new(0),
            CornerRadius = new(12), UseAeroCaptionButtons = false
        });
        window.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left || e.LeftButton != MouseButtonState.Pressed ||
                e.ClickCount != 1 || !CanDragFrom(e.OriginalSource as DependencyObject)) return;
            window.DragMove(); e.Handled = true;
        };
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (HwndSource.FromHwnd(handle) is not { } source) return;
            void Refresh() { if (!source.IsDisposed) ApplyBackdrop(window, source); }
            HwndSourceHook hook = (IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                // Reapply after composition, transparency/accessibility or theme preferences change.
                if (message is 0x031E or 0x001A or 0x031A) window.Dispatcher.BeginInvoke(Refresh);
                return IntPtr.Zero;
            };
            source.AddHook(hook);
            window.Closed += (_, _) => { if (!source.IsDisposed) source.RemoveHook(hook); };
            Refresh();
        };
    }

    internal static bool CanDragFrom(DependencyObject? origin)
    {
        for (var current = origin; current != null; current = Parent(current))
        {
            // Examine ancestors too: a button's icon and a text renderer are separate visuals.
            if (current is TextBoxBase or PasswordBox or ButtonBase or (Selector and not TabControl) or RangeBase or Thumb or
                ListBoxItem or TabItem or MenuItem or Hyperlink) return false;
            if (current is Window) return true;
        }
        return false;
    }
    private static DependencyObject? Parent(DependencyObject item)
    {
        if (item is Visual or Visual3D && VisualTreeHelper.GetParent(item) is { } visual) return visual;
        if (item is FrameworkContentElement content) return content.Parent ?? ContentOperations.GetParent(content);
        return LogicalTreeHelper.GetParent(item);
    }

    internal static bool SupportsGlass(Version version, bool composition, bool transparency, bool highContrast) =>
        version >= new Version(10, 0, 22621) && composition && transparency && !highContrast;

    private static bool TransparencyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int enabled || enabled != 0;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        { return false; }
    }
    private static void ApplyBackdrop(Window window, HwndSource source)
    {
        int dark = 1, rounded = 2, noCaption = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(source.Handle, 20, ref dark, sizeof(int));
        DwmSetWindowAttribute(source.Handle, 33, ref rounded, sizeof(int));
        // Full-client glass can still pick up the system's accent-colored caption fill,
        // even with WindowStyle.None and no reserved caption area. Suppress that part
        // explicitly, keeping the full-client backdrop and native resize border.
        var captionHidden = DwmSetWindowAttribute(source.Handle, 35, ref noCaption, sizeof(int)) == 0;
        var composition = DwmIsCompositionEnabled(out var enabled) == 0 && enabled;
        var useGlass = captionHidden && SupportsGlass(Environment.OSVersion.Version, composition, TransparencyEnabled(), SystemParameters.HighContrast);
        var chrome = WindowChrome.GetWindowChrome(window);
        if (useGlass)
        {
            int acrylic = 3; // DWMSBT_TRANSIENTWINDOW: native Desktop Acrylic on Windows 11.
            var margins = new Margins(-1);
            useGlass = DwmSetWindowAttribute(source.Handle, 38, ref acrylic, sizeof(int)) == 0 &&
                DwmExtendFrameIntoClientArea(source.Handle, ref margins) == 0;
            if (useGlass)
            {
                try { chrome.GlassFrameThickness = new(-1); }
                catch (COMException) { useGlass = false; }
            }
        }
        if (useGlass)
        {
            // Only the background is translucent. Text and controls retain their full opacity.
            window.Background = Brushes.Transparent;
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            window.Resources["WindowSurface"] = new SolidColorBrush(Color.FromArgb(0x28, 0x19, 0x1A, 0x1D));
        }
        else
        {
            window.Background = Brush("Canvas");
            window.Resources["WindowSurface"] = Brush("Canvas");
            source.CompositionTarget.BackgroundColor = ((SolidColorBrush)Brush("Canvas")).Color;
            int none = 1;
            DwmSetWindowAttribute(source.Handle, 38, ref none, sizeof(int));
            try { chrome.GlassFrameThickness = new(0); }
            catch (COMException) { /* A composition transition can reject frame updates; keep the opaque surface. */ }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins(int value)
    { public int Left = value, Right = value, Top = value, Bottom = value; }
    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr handle, ref Margins margins);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
}
