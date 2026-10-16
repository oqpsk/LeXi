using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Lexi.Core;

public sealed class ClaudeDictionary(string assetFolder, string userRoot) : IDisposable
{
    private readonly object sync = new();
    private Worker? prepared;
    private bool disposed;
    private string? desiredModel;

    public static (string Exe, string? Script)? FindExecutable()
    {
        var native = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
        if (File.Exists(native)) return (native, null);
        var cli = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@anthropic-ai", "claude-code", "cli.js");
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var exe = Path.Combine(directory.Trim('"'), "claude.exe");
            if (File.Exists(exe)) return (exe, null);
        }
        if (!File.Exists(cli)) return null;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var node = Path.Combine(directory.Trim('"'), "node.exe");
            if (File.Exists(node)) return (node, cli);
        }
        return null;
    }

    public void Prepare(string model)
    {
        lock (sync)
        {
            desiredModel = model;
            if (disposed || prepared?.Model == model && prepared.IsAlive) return;
            prepared?.Dispose(); prepared = null;
            try { prepared = Start(model); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
    }

    public void ReleasePrepared() { lock (sync) { desiredModel = null; prepared?.Dispose(); prepared = null; } }
    private void Replenish(string model) { lock (sync) { if (!disposed && desiredModel == model) Prepare(model); } }

    private Worker Start(string model)
    {
        var executable = FindExecutable() ?? throw new InvalidOperationException("未找到 Claude Code。请先安装并使用订阅登录。");
        var dir = Path.Combine(userRoot, "claude-work"); Directory.CreateDirectory(dir);
        var info = new ProcessStartInfo(executable.Exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = dir,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        if (executable.Script != null) info.ArgumentList.Add(executable.Script);
        var instructions = File.ReadAllText(Path.Combine(assetFolder, "dictionary.instructions.txt"));
        var schema = File.ReadAllText(Path.Combine(assetFolder, "dictionary.schema.json"));
        foreach (var arg in new[] { "-p", "--model", model, "--tools", "", "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}",
            "--setting-sources", "", "--settings", "{\"disableAllHooks\":true,\"alwaysThinkingEnabled\":false}",
            "--no-chrome", "--no-session-persistence", "--output-format", "stream-json", "--verbose", "--include-partial-messages",
            "--system-prompt", instructions + "\nReturn raw JSON matching this schema, without markdown fences:\n" + schema }) info.ArgumentList.Add(arg);
        // Only the official CLI handles subscription credentials. Lexi never reads or copies tokens.
        foreach (var key in new[] { "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN" }) info.Environment.Remove(key);
        info.Environment["MAX_THINKING_TOKENS"] = "0";
        return new Worker(info, model);
    }

    public async Task<AiEntry> LookupAsync(string query, OnlineEntry? online, string model, CancellationToken token, IProgress<AiEntry>? progress = null)
    {
        Worker worker;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            desiredModel = model;
            if (prepared is { IsAlive: true } && prepared.Model == model) { worker = prepared; prepared = null; }
            else { prepared?.Dispose(); prepared = null; worker = Start(model); }
        }
        using (worker)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(35));
            using var cancel = timeout.Token.Register(worker.Stop);
            var output = new StringBuilder();
            string? final = null, lastPreview = null;
            try
            {
                token.ThrowIfCancellationRequested();
                var prompt = "Define this exact word or phrase. Put current Internet slang before old or rare meanings.\n" +
                    JsonSerializer.Serialize(new { query, source = online?.Url, evidence = CodexDictionary.EvidenceText(online) }, Json.Options);
                await worker.Process.StandardInput.WriteLineAsync(prompt.AsMemory(), timeout.Token);
                worker.Process.StandardInput.Close();
                await foreach (var line in worker.Lines.Reader.ReadAllAsync(timeout.Token))
                {
                    if (line.Length > 300000) throw new InvalidDataException("模型返回内容过长。");
                    using var doc = JsonDocument.Parse(line);
                    var message = doc.RootElement;
                    if (!message.TryGetProperty("type", out var kind)) continue;
                    if (kind.GetString() == "stream_event" && message.TryGetProperty("event", out var ev))
                    {
                        if (ev.TryGetProperty("content_block", out var block) && block.TryGetProperty("type", out var bt) && bt.GetString() == "tool_use")
                            throw new InvalidDataException("词典查询意外请求了工具，已停止。");
                        if (ev.TryGetProperty("delta", out var delta) && delta.TryGetProperty("type", out var dt) && dt.GetString() == "text_delta")
                        {
                            output.Append(delta.GetProperty("text").GetString());
                            if (output.Length > 100000) throw new InvalidDataException("模型返回内容过长。");
                            if (progress != null && StreamingEntry.Read(output.ToString(), query) is { } partial)
                            {
                                var preview = JsonSerializer.Serialize(partial, Json.Options);
                                if (preview != lastPreview) { progress.Report(partial); lastPreview = preview; }
                            }
                        }
                    }
                    if (kind.GetString() == "result")
                    {
                        if (message.TryGetProperty("is_error", out var error) && error.GetBoolean())
                        {
                            var detail = message.TryGetProperty("result", out var result) ? result.GetString() ?? "" : "";
                            if (detail.Contains("401") || detail.Contains("authenticate", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Claude 登录已失效，请在 Claude Code 中重新登录。");
                            throw new InvalidOperationException("Claude 未完成查询，请检查网络或订阅额度。");
                        }
                        final = message.TryGetProperty("result", out var answer) ? answer.GetString() : output.ToString();
                        break;
                    }
                }
                timeout.Token.ThrowIfCancellationRequested();
                return CodexDictionary.ParseAndValidate(StreamingEntry.Unwrap(final ?? throw new InvalidDataException("Claude 未返回完整释义。")), query, online);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("补充查询超时，请重试。"); }
            finally { _ = Task.Run(() => Replenish(model)); }
        }
    }

    public void Dispose() { lock (sync) { disposed = true; prepared?.Dispose(); prepared = null; } }

    private sealed class Worker : IDisposable
    {
        public string Model { get; }
        public Process Process { get; }
        public Channel<string> Lines { get; } = Channel.CreateBounded<string>(512);
        public bool IsAlive { get { try { return !Process.HasExited; } catch (InvalidOperationException) { return false; } } }
        private readonly CancellationTokenSource lifetime = new();
        public Worker(ProcessStartInfo info, string model)
        {
            Model = model; Process = new() { StartInfo = info }; Process.Start();
            var errorReader = Process.StandardError; var outputReader = Process.StandardOutput; var token = lifetime.Token;
            _ = Task.Run(async () =>
            {
                try { while (await errorReader.ReadLineAsync(token) != null) { } }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
            });
            _ = Task.Run(async () =>
            {
                try { while (await outputReader.ReadLineAsync(token) is { } line) await Lines.Writer.WriteAsync(line, token); }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
                finally { Lines.Writer.TryComplete(); }
            });
        }
        public void Stop() { try { if (!Process.HasExited) Process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
        public void Dispose() { lifetime.Cancel(); Stop(); Process.Dispose(); lifetime.Dispose(); }
    }
}
