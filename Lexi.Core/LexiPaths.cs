namespace Lexi.Core;

// Assets ship beside the program. Windows keeps its portable userdata/data folders for
// upgrade compatibility; macOS stores user state and installed dictionaries per user.
public sealed record LexiPaths(string AssetRoot, string DataRoot, string UserRoot)
{
    public static LexiPaths Current => For(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? "");

    public static LexiPaths For(bool windows, bool macOS, string programRoot, string home, string xdgDataHome = "")
    {
        var assets = Path.Combine(programRoot, "assets");
        if (windows) return new(assets, Path.Combine(programRoot, "data"), Path.Combine(programRoot, "userdata"));
        var user = macOS ? Path.Combine(home, "Library", "Application Support", "LeXi")
            : Path.Combine(xdgDataHome.Length > 0 ? xdgDataHome : Path.Combine(home, ".local", "share"), "LeXi");
        return new(assets, Path.Combine(user, "data"), user);
    }

    public LexiPaths With(string? assets = null, string? data = null, string? user = null) =>
        new(Full(assets) ?? AssetRoot, Full(data) ?? DataRoot, Full(user) ?? UserRoot);
    private static string? Full(string? path) => string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
}
