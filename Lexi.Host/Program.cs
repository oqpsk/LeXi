using System.Text;
using Lexi.Core;
using Lexi.Host;

// lexi-host [--data <folder>] [--user <folder>] [--assets <folder>]
// Speaks JSON lines on stdin/stdout; see docs/host-protocol.md.
if (args is ["--version"]) { Console.WriteLine(HostServer.Version); return 0; }
var options = new Dictionary<string, string>();
for (int i = 0; i < args.Length; i += 2)
{
    if (args[i] is not ("--data" or "--user" or "--assets") || i + 1 >= args.Length || !options.TryAdd(args[i], args[i + 1]))
    {
        Console.Error.WriteLine("usage: lexi-host [--data <folder>] [--user <folder>] [--assets <folder>]");
        return 2;
    }
}
var paths = LexiPaths.Current.With(options.GetValueOrDefault("--assets"), options.GetValueOrDefault("--data"), options.GetValueOrDefault("--user"));
try { Directory.CreateDirectory(paths.UserRoot); }
catch (Exception e) when (e is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine("lexi-host: the user data folder cannot be created.");
    return 3;
}
var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = false };
// Only protocol messages reach stdout; any stray console output goes to stderr.
Console.SetOut(Console.Error);
using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
using var session = new QuerySession(paths);
await new HostServer(session, input, output).RunAsync();
await output.DisposeAsync();
return 0;
