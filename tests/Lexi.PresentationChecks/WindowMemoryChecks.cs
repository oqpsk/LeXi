using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Lexi;

internal static partial class Program
{
    private static void CheckWindowMemory()
    {
        void Check(bool ok, string text) { if (!ok) throw new Exception("FAIL: " + text); Console.WriteLine("PASS: " + text); }
        var folder = Path.Combine(Path.GetTempPath(), "lexi-window-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "window-placement.json");
            var preferences = Path.Combine(folder, "settings.json"); File.WriteAllText(preferences, "preserve");
            Check(WindowPosition.Load(file) == null, "new installs use the normal centered placement");
            var saved = new WindowPosition(-1800, 80, -900, 780, false);
            Check(saved.Save(file) && WindowPosition.Load(file) == saved, "window bounds persist across reload with negative monitor coordinates intact");
            var larger = saved with { Right = -650, Bottom = 980, Maximized = true };
            Check(larger.Save(file) && WindowPosition.Load(file) == larger && File.ReadAllText(preferences) == "preserve" && Directory.GetFiles(folder).Length == 2,
                "new window placement atomically replaces old bounds without touching settings or leaving temporary files");
            var minimized = WindowPlacement.ToNative(larger); minimized.Show = 2; minimized.Flags = 2;
            Check(WindowPlacement.FromNative(minimized) == larger && WindowPlacement.ToNative(WindowPlacement.FromNative(minimized)!).Show == 3,
                "minimized maximized windows retain their normal bounds and reopen maximized");
            minimized.Flags = 0;
            Check(WindowPlacement.FromNative(minimized) == larger with { Maximized = false } && WindowPlacement.ToNative(WindowPlacement.FromNative(minimized)!).Show == 1,
                "minimization never becomes the next launch state or overwrites normal dimensions");
            Check(WindowPlacement.FromNative(WindowPlacement.ToNative(saved)) == saved,
                "native workspace placement round trips without mixing taskbar offsets and WPF coordinates");
            foreach (var bad in new[] { saved with { Right = saved.Left }, saved with { Bottom = int.MaxValue }, saved with { Version = 2 } })
                Check(!bad.Save(file) && WindowPosition.Load(file) == larger, "invalid placement cannot replace a good saved window");
            foreach (var json in new[] { "{", "null", "{}", "{\"left\":0,\"top\":0,\"right\":-20,\"bottom\":800}" })
            { File.WriteAllText(file,json); Check(WindowPosition.Load(file)==null,"damaged window state safely falls back to default placement"); }
        }
        finally { Directory.Delete(folder, true); }
    }
    private static void CheckNativeWindowMemory()
    {
        void Check(bool ok, string text) { if (!ok) throw new Exception("FAIL: " + text); Console.WriteLine("PASS: " + text); }
        var folder = Path.Combine(Path.GetTempPath(), "lexi-native-placement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "window-placement.json");
        Window MakeWindow() => new() { Width = 720, Height = 620, MinWidth = 560, MinHeight = 400, ShowInTaskbar = false };
        WindowPosition Read(IntPtr hwnd)
        {
            var placement = new WindowPlacement.Placement { Length = Marshal.SizeOf<WindowPlacement.Placement>() };
            if (!ReadWindowPlacement(hwnd, ref placement)) throw new Exception("Native placement read failed.");
            return WindowPlacement.FromNative(placement) ?? throw new Exception("Invalid native bounds.");
        }
        void SetHidden(IntPtr hwnd, WindowPosition position)
        {
            var placement = WindowPlacement.ToNative(position); placement.Show = 0;
            if (!WriteWindowPlacement(hwnd, ref placement) || IsWindowVisible(hwnd)) throw new Exception("Hidden native placement failed.");
        }
        try
        {
            // Obtain monitor-safe native coordinates without creating a visible window.
            var seed = MakeWindow();
            WindowPosition expected;
            try
            {
                var handle = new WindowInteropHelper(seed).EnsureHandle();
                var initial = Read(handle);
                SetHidden(handle, initial with { Left = initial.Left + 37, Top = initial.Top + 29, Right = initial.Right + 101, Bottom = initial.Bottom + 83 });
                expected = Read(handle);
                Check(!IsWindowVisible(handle) && expected.Save(file), "native placement fixture is saved without displaying a window");
            }
            finally { seed.Close(); }

            var first = MakeWindow(); Theme.ApplyChrome(first);
            using (var memory = new WindowPlacement(first, file))
            {
                try
                {
                    var handle = new WindowInteropHelper(first).EnsureHandle();
                    Check(!IsWindowVisible(handle), "initial placement restoration never shows a hidden HWND");
                    // Emulate late startup layout replacing staged native bounds. Early
                    // Loaded/size notifications must not overwrite the startup snapshot.
                    first.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                    first.Width = 720; first.Height = 620;
                    SetHidden(handle, expected with { Right = expected.Right + 60, Bottom = expected.Bottom + 80 });
                    Check(WindowPosition.Load(file) == expected, "startup layout changes cannot overwrite the saved placement before restoration");
                    memory.CompleteStartup();
                    Check(Read(handle) == expected && WindowPosition.Load(file) == expected && !IsWindowVisible(handle),
                        "final startup restoration restores native size and position after chrome and layout changes");
                    SetHidden(handle, expected with { Left = expected.Left + 13, Top = expected.Top + 17, Right = expected.Right + 57, Bottom = expected.Bottom + 49 });
                    expected = Read(handle);
                    memory.CompleteStartup();
                    Check(Read(handle) == expected, "subsequent rendered notifications never snap back a moved window");
                }
                finally { first.Close(); }
            }
            Check(WindowPosition.Load(file) == expected, "closing flushes the final moved and resized native bounds");
            var reopened = MakeWindow(); Theme.ApplyChrome(reopened);
            using (var memory = new WindowPlacement(reopened, file))
            {
                try
                {
                    var handle = new WindowInteropHelper(reopened).EnsureHandle();
                    memory.CompleteStartup();
                    Check(Read(handle) == expected && !IsWindowVisible(handle), "a newly created HWND restores the previous window bounds from disk");
                }
                finally { reopened.Close(); }
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowPlacement")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadWindowPlacement(IntPtr hwnd, ref WindowPlacement.Placement placement);
    [DllImport("user32.dll", EntryPoint = "SetWindowPlacement")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteWindowPlacement(IntPtr hwnd, ref WindowPlacement.Placement placement);

}
