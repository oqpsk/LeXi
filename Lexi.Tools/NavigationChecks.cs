using Lexi.Core;

internal static class NavigationChecks
{
    public static void Run(string root)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
        var trail = new QueryNavigation();
        Check(trail.Current.Query == "" && !trail.CanBack && !trail.CanForward && trail.Move(-1) == null,
            "session navigation begins at home with disabled boundaries");
        trail.Visit("TEE"); trail.RememberScroll(143); trail.Visit("tee");
        Check(trail.Move(-1) is { Query: "TEE", ScrollOffset: 143 } && trail.CanForward,
            "back navigation preserves case and reading position");
        var revision = trail.Revision; trail.Visit("TEE");
        Check(trail.Forward?.Query == "tee" && trail.Revision > revision && trail.Back?.Query == "",
            "same-page refresh preserves forward history without adding a duplicate visit");
        Check(trail.Move(1)?.Query == "tee" && trail.Move(1) == null && trail.Move(2) == null,
            "forward navigation respects boundaries and rejects non-navigation offsets");
        trail.Move(-1); trail.Visit("New sentence.\n\nSecond paragraph.", true);
        Check(!trail.CanForward && trail.Back?.Query == "TEE" && trail.Current is { Translation: true, Query: "New sentence.\n\nSecond paragraph." },
            "new text after going back discards only the forward branch and retains paragraphs/routing");
        trail.Visit(""); trail.Move(-1);
        Check(trail.Current.Translation && trail.Forward?.Query == "", "home participates in navigation without becoming a dictionary query");
        trail.RememberScroll(double.NaN);
        Check(trail.Current.ScrollOffset == 0, "invalid scroll positions cannot corrupt page restoration");
        for (int i = 0; i < 150; i++) trail.Visit("entry" + i);
        int steps = 0; while (trail.Move(-1) != null) steps++;
        Check(steps == QueryNavigation.Capacity - 1 && trail.Current.Query == "entry50", "the in-memory navigation trail has a bounded lifetime and capacity");
        var work = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "settings.json"), "{\"online\":false}");
        var settings = AppSettings.Load(work);
        Check(settings.MinimizeOnClose && !settings.Online, "legacy settings preserve taskbar minimization as the close default");
        settings.MinimizeOnClose = false; settings.Save(work);
        Check(!AppSettings.Load(work).MinimizeOnClose && !AppSettings.Load(work).Online,
            "explicit exit-on-close preference persists without changing online mode");
        settings.MinimizeOnClose = true; settings.Save(work);
        Check(AppSettings.Load(work).MinimizeOnClose, "close preference can be changed back to minimize");
    }
}
