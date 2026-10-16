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
        var translationSchema = File.ReadAllText(Path.Combine(assetFolder, "translation.schema.json"));
        foreach (var arg in new[] { "-p", "--model", model, "--tools", "", "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}",
            "--setting-sources", "", "--settings", "{\"disableAllHooks\":true,\"alwaysThinkingEnabled\":false}",
            "--no-chrome", "--no-session-persistence", "--output-format", "stream-json", "--verbose", "--include-partial-messages",
            "--system-prompt", instructions + "\nFor define, use this JSON schema:\n" + schema + "\nFor translate, use this JSON schema:\n" + translationSchema }) info.ArgumentList.Add(arg);
        // Only the official CLI handles subscription credentials. Lexi never reads or copies tokens.
        foreach (var key in new[] { "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN" }) info.Environment.Remove(key);
        info.Environment["MAX_THINKING_TOKENS"] = "0";
        return new Worker(info, model);
    }

    public async Task<AiEntry> LookupAsync(string query, OnlineEntry? online, string model, CancellationToken token, IProgress<AiEntry>? progress = null)
    {
        string? last = null;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var result = await RunAsync(CodexDictionary.DefineRequest(query, online) + RetryInstruction(attempt), model, token, text =>
                {
                    if (progress != null && StreamingEntry.Read(text, query) is { } preview)
                    { var value = JsonSerializer.Serialize(preview, Json.Options); if (last != value) { last = value; progress.Report(preview); } }
                });
                return CodexDictionary.ParseAndValidate(StreamingEntry.Unwrap(result), query, online);
            }
            catch (Exception ex) when (attempt == 0 && !token.IsCancellationRequested && ex is JsonException or InvalidDataException) { last = null; }
        }
    }
    public async Task<TranslationEntry> TranslateAsync(string text, string target, string model, CancellationToken token, IProgress<TranslationEntry>? progress = null)
    {
        string? last = null;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var result = await RunAsync(Translation.Request(text, target) + RetryInstruction(attempt), model, token, json =>
                {
                    if (progress != null && Translation.Preview(json, text, target) is { } preview)
                    { var value = JsonSerializer.Serialize(preview, Json.Options); if (last != value) { last = value; progress.Report(preview); } }
                });
                return Translation.Parse(result, text, target);
            }
            catch (Exception ex) when (attempt == 0 && !token.IsCancellationRequested && ex is JsonException or InvalidDataException) { last = null; }
        }
    }
    private static string RetryInstruction(int attempt) => attempt == 0 ? "" : "\nThe previous response failed structural validation. Regenerate one complete valid JSON object. Escape every embedded double quote and newline inside strings. Follow the requested schema and exact input identity. No commentary or markdown.";
    private async Task<string> RunAsync(string prompt, string model, CancellationToken token, Action<string>? preview)
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
            timeout.CancelAfter(TimeSpan.FromSeconds(75));
            using var cancel = timeout.Token.Register(worker.Stop);
            var output = new StringBuilder();
            string? final = null;
            try
            {
                token.ThrowIfCancellationRequested();
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
                            preview?.Invoke(output.ToString());
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
                return final ?? throw new InvalidDataException("Claude 未返回完整结果。");
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
