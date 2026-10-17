using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Lexi.Core;
using Lexi.Host;

// The JSON-lines protocol used by the macOS app: in process with synthetic providers for
// streaming and cancellation, then the built host as a separate process.
internal static class HostChecks
{
    public static async Task Run(string fixture, string root, string hostAssembly)
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
        var work = Path.Combine(Path.GetFullPath(root), Guid.NewGuid().ToString("N"));
        var blocked = 0;
        var providers = new QueryProviders(
            async (model, query, evidence, options, token, preview) =>
            {
                // The first "hello" model call waits until cancelled; later visits complete normally.
                if (query == "hello" && Interlocked.Exchange(ref blocked, 1) == 0) await Task.Delay(Timeout.Infinite, token);
                var entry = new AiEntry(query, true, [new("noun", model.Name + " meaning", "合成释义：" + model.Name, "", "", "", [])], "", "unverified", WordForms: []);
                preview.Report(entry); await Task.Yield(); return entry;
            },
            (request, options, token) => Task.FromResult(new SenseMergePlan(request.InputHash, request.Pairs.Select(p => new SenseVerdict(p.A, p.B, "equivalent", 0.99)).ToArray())),
            async (text, target, options, token, preview) =>
            {
                var entry = new TranslationEntry(Cache.Hash(text), target, ["船已就绪。", "保留 42。"], "");
                preview.Report(entry with { Paragraphs = ["船已就绪。"] }); await Task.Yield(); return entry;
            });
        var user = Path.Combine(work, "in-process");
        using (var session = new QuerySession(new(Path.Combine(work, "assets"), Path.GetFullPath(fixture), user), providers: providers, network: new NoNetwork()))
        {
            var client = new Client();
            var server = new HostServer(session, client.Input, client.Output).RunAsync();
            var init = await client.Call("initialize");
            Check(init.GetProperty("result") is var ready && ready.GetProperty("protocol").GetInt32() == HostServer.Protocol &&
                ready.GetProperty("view").GetProperty("kind").GetString() == "home" &&
                ready.GetProperty("settings").GetProperty("models").EnumerateArray().Any(m => m.GetProperty("label").GetString() == "GPT-6 Luna · Fast") &&
                ready.GetProperty("settings").GetProperty("controls").GetProperty("onlineLabel").GetString() == "在线补充",
                "initialize reports the protocol version, the home view, shared model choices and control labels");
            Check((await client.Call("suggest", new { text = "wal" })).GetProperty("result").GetProperty("suggestions").EnumerateArray().Any(s => s.GetString() == "walk"),
                "suggestions come from the shared offline library");

            var submitted = (await client.Call("query.submit", new { text = "serendipity" })).GetProperty("result");
            var request = submitted.GetProperty("request").GetInt64();
            var views = client.Views(request);
            var final = views.Last();
            Check(final.GetProperty("revision").GetInt64() == submitted.GetProperty("revision").GetInt64() &&
                views.Select(v => v.GetProperty("revision").GetInt64()).Zip(views.Skip(1).Select(v => v.GetProperty("revision").GetInt64())).All(p => p.First < p.Second),
                "a submission streams ordered views and answers with the revision of its final view");
            Check(final.GetProperty("kind").GetString() == "dictionary" && final.GetProperty("progress") is var done &&
                done.GetProperty("outcome").GetString() == "complete" && !done.GetProperty("busy").GetBoolean() &&
                done.GetProperty("title").GetString() == "已完成 · 1 个义项" && done.GetProperty("stageLabels").GetArrayLength() == 4 &&
                views.Any(v => v.GetProperty("progress") is { ValueKind: JsonValueKind.Object } p && p.GetProperty("stage").GetString() == "supplement" && p.GetProperty("busy").GetBoolean()),
                "views carry camelCase fields, string enums and the same lifecycle as the Windows window");
            Check(final.GetProperty("answers").GetProperty("senses")[0].GetProperty("alternativesLabel").GetString() == "其他表述 · 1" &&
                final.GetProperty("quickRead").GetProperty("caption").GetString() == "TL;DR · 已整理",
                "merged senses and the overview reach the front end fully worded");

            var pending = client.Send("query.submit", new { text = "hello" });
            await client.Until(v => v.GetProperty("query").GetString() == "hello" && v.GetProperty("progress").GetProperty("stage").GetString() == "supplement");
            var cancelled = await client.Call("cancel", new { id = pending.Id });
            var interrupted = await pending.Response;
            var stopped = client.Views(interrupted.GetProperty("result").GetProperty("request").GetInt64()).Last().GetProperty("progress");
            Check(cancelled.GetProperty("result").GetProperty("cancelled").GetBoolean() && stopped.GetProperty("title").GetString() == "查询已中断" &&
                !stopped.GetProperty("busy").GetBoolean(), "cancelling a running submission ends the query with an interrupted, settled state");

            var translated = (await client.Call("query.submit", new { text = "The ship is ready.\nKeep 42 intact." })).GetProperty("result");
            var translationViews = client.Views(translated.GetProperty("request").GetInt64());
            Check(translationViews.Last().GetProperty("translation").GetProperty("paragraphs").GetArrayLength() == 2 &&
                translationViews.Any(v => v.GetProperty("translation") is { ValueKind: JsonValueKind.Object } t && !t.GetProperty("complete").GetBoolean()),
                "translation previews stream before the complete paragraphs");
            var history = (await client.Call("history.list")).GetProperty("result").GetProperty("entries");
            Check(history[0].GetProperty("kind").GetString() == "翻译" && history[0].GetProperty("preview").GetString() == "The ship is ready. Keep 42 intact." &&
                (await client.Call("history.remove", new { query = history[0].GetProperty("query").GetString(), translation = true })).GetProperty("result").GetProperty("removed").GetBoolean(),
                "history exposes previews and kinds and removes single entries");

            var page = (await client.Call("query.navigate", new { direction = -1 })).GetProperty("result").GetProperty("page");
            Check(page.GetProperty("query").GetString() == "hello" &&
                (await client.Call("query.navigate", new { direction = 2 })).GetProperty("error").GetProperty("code").GetString() == "invalid_params",
                "navigation moves through the session trail and rejects other offsets");

            var duplicate = await client.Call("settings.update", new { settings = new { parallelLookup = true, secondaryProvider = "codex", secondaryModel = "" } });
            Check(duplicate.GetProperty("error").GetProperty("code").GetString() == "invalid_settings" &&
                duplicate.GetProperty("error").GetProperty("message").GetString() == "请选择两个不同的查询模型。",
                "settings use the same validation message as the Windows dialog");
            Check((await client.Call("settings.update", new { settings = new { unknown = 1 } })).GetProperty("error").GetProperty("code").GetString() == "invalid_params" &&
                (await client.Call("settings.update", new { settings = new { online = "yes" } })).GetProperty("error").GetProperty("code").GetString() == "invalid_params",
                "unknown settings and wrong value types are rejected without saving");
            var updated = (await client.Call("settings.update", new { settings = new { mergeSenses = false, reasoningEffort = "high" } })).GetProperty("result").GetProperty("settings");
            Check(!updated.GetProperty("mergeSenses").GetBoolean() && updated.GetProperty("reasoningEffort").GetString() == "low" &&
                !AppSettings.Load(user).MergeSenses, "valid settings are saved with the fixed Codex options");
            var offline = (await client.Call("settings.setOnline", new { online = false })).GetProperty("result");
            Check(offline.GetProperty("controls").GetProperty("onlineLabel").GetString() == "离线查询" && !AppSettings.Load(user).Online &&
                client.Latest().GetProperty("status").GetString()!.StartsWith("查询模式已更改"), "the online switch saves and keeps the current result");

            var login = (await client.Call("connection.loginCommand", new { provider = "codex", codexPath = Environment.ProcessPath })).GetProperty("result");
            Check(login.GetProperty("fileName").GetString() == Environment.ProcessPath && login.GetProperty("arguments").EnumerateArray().Select(a => a.GetString()).SequenceEqual(["login"]) &&
                (await client.Call("connection.status", new { provider = "other" })).GetProperty("error").GetProperty("code").GetString() == "invalid_params",
                "sign-in commands come from the shared client discovery and unknown providers are refused");
            Check((await client.Call("dictionaries.list")).GetProperty("result").GetProperty("dictionaries").EnumerateArray().Single().GetProperty("available").GetBoolean(),
                "installed dictionaries are listed for settings");

            client.Raw("not json"); client.Raw("{\"method\":\"query.view\"}");
            var unknown = await client.Call("query.unknown");
            Check(client.Orphans.Count == 2 && client.Orphans.Select(o => o.GetProperty("error").GetProperty("code").GetString()).Order().SequenceEqual(["invalid_request", "parse_error"]) &&
                unknown.GetProperty("error").GetProperty("code").GetString() == "method_not_found", "malformed, anonymous and unknown requests get protocol errors");
            var shutdown = client.Send("shutdown");
            await server;
            Check((await shutdown.Response).TryGetProperty("result", out _) && client.LastMessageId == shutdown.Id, "shutdown is answered last and ends the server");
        }

        // The built executable, offline: argument handling, stdout hygiene and exit on end of input.
        var host = Path.Combine(work, "process");
        Directory.CreateDirectory(host);
        File.WriteAllText(Path.Combine(host, "settings.json"), "{\"online\":false}");
        var (exit, lines, errors) = await RunHost(hostAssembly, ["--data", Path.GetFullPath(fixture), "--user", host],
            ["{\"id\":1,\"method\":\"initialize\"}", "{\"id\":2,\"method\":\"query.submit\",\"params\":{\"text\":\"walked\"}}", "{\"id\":3,\"method\":\"shutdown\"}"]);
        var messages = lines.Select(l => JsonDocument.Parse(l).RootElement).ToArray();
        var last = messages.Where(m => m.TryGetProperty("event", out _)).Last().GetProperty("data");
        Check(exit == 0 && errors.Length == 0 && last.GetProperty("local")[0].GetProperty("chinese").GetString() == "步行" &&
            last.GetProperty("wordForms")[0].GetProperty("cells")[0].GetProperty("spelling").GetString() == "walked → walk" &&
            messages.Last().GetProperty("id").GetInt32() == 3, "the host process answers on stdout only and exits after shutdown");
        (exit, lines, _) = await RunHost(hostAssembly, ["--user", host, "--data", Path.GetFullPath(fixture)], ["{\"id\":1,\"method\":\"query.view\"}"]);
        Check(exit == 0 && lines.Length == 1, "closing standard input ends the host without a shutdown request");
        (exit, _, errors) = await RunHost(hostAssembly, ["--unknown"], []);
        Check(exit == 2 && errors.Contains("usage"), "unknown arguments are rejected before any protocol output");
        Console.WriteLine($"{count} host protocol checks passed");
    }

    private static async Task<(int Exit, string[] Lines, string Errors)> RunHost(string assembly, string[] arguments, string[] requests)
    {
        // A framework-dependent build runs through dotnet; a published single-file host runs directly.
        var managed = assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var info = new ProcessStartInfo(managed ? "dotnet" : Path.GetFullPath(assembly)) { RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8 };
        if (managed) info.ArgumentList.Add(Path.GetFullPath(assembly));
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        foreach (var request in requests) await process.StandardInput.WriteLineAsync(request);
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, (await output).Split('\n', StringSplitOptions.RemoveEmptyEntries), await errors);
    }

    private sealed class Client
    {
        private readonly Channel<string?> requests = Channel.CreateUnbounded<string?>();
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> waiting = new();
        private readonly List<JsonElement> views = [];
        private int next;
        public LineReader Input { get; }
        public LineWriter Output { get; }
        public List<JsonElement> Orphans { get; } = [];
        public int LastMessageId { get; private set; }
        public Client() { Input = new(requests); Output = new(Receive); }

        public sealed record Pending(int Id, Task<JsonElement> Response);
        public Pending Send(string method, object? parameters = null)
        {
            var id = Interlocked.Increment(ref next);
            var response = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            waiting[id] = response;
            Raw(JsonSerializer.Serialize(new { id, method, @params = parameters }));
            return new(id, response.Task);
        }
        public async Task<JsonElement> Call(string method, object? parameters = null) =>
            await Send(method, parameters).Response.WaitAsync(TimeSpan.FromSeconds(30));
        public void Raw(string line) => requests.Writer.TryWrite(line);

        public List<JsonElement> Views(long request) { lock (views) return views.Where(v => v.GetProperty("request").GetInt64() == request).ToList(); }
        public JsonElement Latest() { lock (views) return views[^1]; }
        public async Task Until(Func<JsonElement, bool> condition)
        {
            for (var i = 0; i < 500; i++) { lock (views) if (views.Any(condition)) return; await Task.Delay(10); }
            throw new TimeoutException("FAIL: expected host view was not published");
        }

        private void Receive(string line)
        {
            var message = JsonDocument.Parse(line).RootElement.Clone();
            if (message.TryGetProperty("event", out _)) { lock (views) views.Add(message.GetProperty("data")); return; }
            if (message.GetProperty("id").ValueKind != JsonValueKind.Number) { lock (Orphans) Orphans.Add(message); return; }
            LastMessageId = message.GetProperty("id").GetInt32();
            if (waiting.TryRemove(LastMessageId, out var response)) response.TrySetResult(message);
        }
    }

    private sealed class LineReader(Channel<string?> lines) : TextReader
    {
        public override async ValueTask<string?> ReadLineAsync(CancellationToken token)
        {
            try { return await lines.Reader.ReadAsync(token); }
            catch (ChannelClosedException) { return null; }
        }
    }

    private sealed class LineWriter(Action<string> receive) : TextWriter
    {
        private readonly StringBuilder pending = new();
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value)
        {
            lock (pending)
            {
                if (value != '\n') { pending.Append(value); return; }
                var line = pending.ToString(); pending.Clear(); receive(line);
            }
        }
        public override void Write(string? value) { foreach (var c in value ?? "") Write(c); }
        public override Task WriteAsync(string? value) { Write(value); return Task.CompletedTask; }
        public override Task WriteAsync(char value) { Write(value); return Task.CompletedTask; }
        public override Task FlushAsync() => Task.CompletedTask;
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
    }
}
