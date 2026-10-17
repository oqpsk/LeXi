using System.Diagnostics;
using System.Text;

namespace Lexi.Core;

// Root locates system folders such as /opt/homebrew and /Applications, so checks can use a synthetic tree.
public sealed record ToolEnvironment(bool Windows, string Home, string PathVariable, string AppData, string LocalAppData, string NpmPrefix = "", string Root = "/")
{
    public static ToolEnvironment Current => new(OperatingSystem.IsWindows(),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable("PATH") ?? "",
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetEnvironmentVariable("NPM_CONFIG_PREFIX") ?? Environment.GetEnvironmentVariable("npm_config_prefix") ?? "");
}

// A located official client. Script is set when a Node entry point must run through node.
public sealed record ToolCommand(string FileName, string? Script = null, string[]? SearchPath = null)
{
    public ProcessStartInfo StartInfo()
    {
        var info = new ProcessStartInfo(FileName);
        if (Script != null) info.ArgumentList.Add(Script);
        // An app opened from Finder does not inherit the login shell PATH; npm shims and the
        // clients' own helpers need the directories where they were found.
        if (SearchPath is { Length: > 0 })
        {
            var inherited = info.Environment.TryGetValue("PATH", out var value) ? value ?? "" : "";
            info.Environment["PATH"] = string.Join(Path.PathSeparator, SearchPath.Concat(inherited.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)).Distinct());
        }
        return info;
    }
}

public static class ToolLocator
{
    public static ToolCommand? Codex(string configured = "", ToolEnvironment? environment = null)
    {
        var env = environment ?? ToolEnvironment.Current;
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? Resolve(configured, env) : null;
        if (env.Windows)
        {
            if (Find(env, "codex.exe") is { } onPath) return new(onPath);
            var root = Path.Combine(env.LocalAppData, "OpenAI", "Codex", "bin");
            if (!Directory.Exists(root)) return null;
            return Directory.EnumerateDirectories(root).OrderByDescending(Directory.GetLastWriteTimeUtc)
                .Select(x => Path.Combine(x, "codex.exe")).Where(File.Exists).Select(x => new ToolCommand(x)).FirstOrDefault();
        }
        if (Find(env, "codex") is { } path) return Resolve(path, env);
        // The ChatGPT desktop app ships the official Codex CLI, as the Codex app does on Windows.
        return new[] { Path.Combine(env.Home, "Applications"), Path.Combine(env.Root, "Applications") }
            .Select(apps => Path.Combine(apps, "ChatGPT.app", "Contents", "Resources", "codex-cli", "bin", "codex"))
            .Where(File.Exists).Select(app => Resolve(app, env)).FirstOrDefault();
    }

    public static ToolCommand? Claude(ToolEnvironment? environment = null)
    {
        var env = environment ?? ToolEnvironment.Current;
        if (env.Windows)
        {
            var native = Path.Combine(env.Home, ".local", "bin", "claude.exe");
            if (File.Exists(native)) return new(native);
            if (Find(env, "claude.exe") is { } exe) return new(exe);
            var cli = Path.Combine(env.AppData, "npm", "node_modules", "@anthropic-ai", "claude-code", "cli.js");
            return File.Exists(cli) && Find(env, "node.exe") is { } node ? new(node, cli) : null;
        }
        foreach (var native in new[] { Path.Combine(env.Home, ".local", "bin", "claude"), Path.Combine(env.Home, ".claude", "local", "claude") })
            if (File.Exists(native)) return Resolve(native, env);
        if (Find(env, "claude") is { } path) return Resolve(path, env);
        var module = Prefixes(env).Select(p => Path.Combine(p, "lib", "node_modules", "@anthropic-ai", "claude-code", "cli.js")).FirstOrDefault(File.Exists);
        if (module != null) return Resolve(module, env);
        return DesktopClaude(env) is { } desktop ? Resolve(desktop, env) : null;
    }

    // The Claude desktop app keeps verified Claude Code builds in claude-code/<version>/<build>/claude.app.
    // claude-code-vm holds Linux builds for its sandbox and is never used here.
    private static string? DesktopClaude(ToolEnvironment env)
    {
        var root = Path.Combine(env.Home, "Library", "Application Support", "Claude", "claude-code");
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateDirectories(root)
            .OrderByDescending(v => Version.TryParse(Path.GetFileName(v), out var version) ? version : new Version())
            .ThenByDescending(Directory.GetLastWriteTimeUtc)
            .SelectMany(v => Directory.EnumerateDirectories(v).OrderByDescending(Directory.GetLastWriteTimeUtc))
            .Where(build => File.Exists(Path.Combine(build, ".verified")))
            .Select(build => Path.Combine(build, "claude.app", "Contents", "MacOS", "claude"))
            .FirstOrDefault(File.Exists);
    }

    // PATH first, then the usual install locations that a Finder-launched app cannot see.
    public static string[] SearchDirectories(ToolEnvironment env)
    {
        var path = env.PathVariable.Split(env.Windows ? ';' : ':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Trim('"'));
        if (env.Windows) return path.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var home = env.Home;
        var managers = new[] { Path.Combine(home, ".nvm", "versions", "node"), Path.Combine(home, ".local", "share", "fnm", "node-versions"),
            Path.Combine(home, "Library", "Application Support", "fnm", "node-versions") }
            .Where(Directory.Exists).SelectMany(root => Directory.EnumerateDirectories(root).OrderByDescending(Directory.GetLastWriteTimeUtc)
                .SelectMany(v => new[] { Path.Combine(v, "bin"), Path.Combine(v, "installation", "bin") }));
        return path.Concat([Path.Combine(home, ".local", "bin"), Path.Combine(env.Root, "opt", "homebrew", "bin"), Path.Combine(env.Root, "usr", "local", "bin")])
            .Concat(Prefixes(env).Select(p => Path.Combine(p, "bin")))
            .Concat([Path.Combine(home, ".volta", "bin"), Path.Combine(home, ".bun", "bin"), Path.Combine(home, "Library", "pnpm")])
            .Concat(managers).Where(d => d.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
    }

    // npm global prefixes: explicit configuration, a user prefix, Homebrew and the official installer.
    private static IEnumerable<string> Prefixes(ToolEnvironment env) => new[] { env.NpmPrefix, Path.Combine(env.Home, ".npm-global"),
        Path.Combine(env.Home, ".npm-packages"), Path.Combine(env.Root, "opt", "homebrew"), Path.Combine(env.Root, "usr", "local") }.Where(p => p.Length > 0);

    private static string? Find(ToolEnvironment env, string name) =>
        SearchDirectories(env).Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);

    private static ToolCommand Resolve(string path, ToolEnvironment env)
    {
        var directories = SearchDirectories(env);
        if (env.Windows || !NodeScript(path)) return new(path, null, env.Windows ? null : [Path.GetDirectoryName(path)!, .. directories]);
        // Prefer node from the same npm prefix as the client (beside a bin/ shim, or <prefix>/bin for
        // <prefix>/lib/node_modules/…), then any searched directory such as Homebrew's.
        var folder = Path.GetDirectoryName(path)!;
        var modules = path.IndexOf(Path.DirectorySeparatorChar + Path.Combine("lib", "node_modules") + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        var prefix = modules > 0 ? new[] { Path.Combine(path[..modules], "bin") } : [];
        var node = new[] { folder }.Concat(prefix).Concat(directories).Select(d => Path.Combine(d, "node")).FirstOrDefault(File.Exists);
        return node == null ? new(path, null, [folder, .. directories]) : new(node, path, [Path.GetDirectoryName(node)!, folder, .. directories]);
    }

    private static bool NodeScript(string path)
    {
        if (Path.GetExtension(path) is ".js" or ".mjs" or ".cjs") return true;
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[128];
            var read = stream.Read(buffer);
            if (read < 2 || buffer[0] != '#' || buffer[1] != '!') return false;
            var line = Encoding.UTF8.GetString(buffer, 0, read).Split('\n')[0];
            return line.Contains("node", StringComparison.Ordinal);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
