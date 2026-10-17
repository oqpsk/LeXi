using System.IO;
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
}
