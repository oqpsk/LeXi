using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Lexi.Core;

// Claude Code's --settings argument.
internal sealed record ClaudeCliSettings(bool DisableAllHooks, bool FastMode);

public sealed class ClaudeDictionary(string assetFolder, string userRoot) : IDisposable
{
    private readonly object sync = new();
    private Worker? prepared;
    private bool disposed;
    private string? desiredModel;

    public void Prepare(string model) => Prepare(new AppSettings { Provider = "claude", Model = model });
    public Task<AiEntry> LookupAsync(string query, OnlineEntry? online, string model, CancellationToken token, IProgress<AiEntry>? progress = null) =>
        LookupAsync(query, online, new AppSettings { Provider = "claude", Model = model }, token, progress);
    public Task<TranslationEntry> TranslateAsync(string text, string target, string model, CancellationToken token, IProgress<TranslationEntry>? progress = null) =>
        TranslateAsync(text, target, new AppSettings { Provider = "claude", Model = model }, token, progress);
    public void Prepare(AppSettings settings)
    {
        lock (sync)
        {
            var model = Identity(settings); desiredModel = model;
            if (disposed || prepared?.Model == model && prepared.IsAlive) return;
            prepared?.Dispose(); prepared = null;
            try { prepared = Start(settings); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
    }

    public void ReleasePrepared() { lock (sync) { desiredModel = null; prepared?.Dispose(); prepared = null; } }
    private static string Identity(AppSettings settings) => settings.Model + ":" + InferenceOptions.Identity(settings, LookupModel.Primary(settings));
    private void Replenish(AppSettings settings) { lock (sync) { if (!disposed && desiredModel == Identity(settings)) Prepare(settings); } }

    private Worker Start(AppSettings settings)
    {
        InferenceOptions.ValidateRequest(settings, userRoot);
        var selected = InferenceOptions.Selected(settings, LookupModel.Primary(settings));
        var tool = ToolLocator.Claude() ?? throw new InvalidOperationException("未找到 Claude Code。请先安装并使用订阅登录。");
        var dir = Path.Combine(userRoot, "claude-work"); Directory.CreateDirectory(dir);
        var info = tool.StartInfo();
        info.UseShellExecute = false; info.CreateNoWindow = true; info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true; info.RedirectStandardError = true; info.WorkingDirectory = dir;
        info.StandardInputEncoding = new UTF8Encoding(false); info.StandardOutputEncoding = Encoding.UTF8; info.StandardErrorEncoding = Encoding.UTF8;
        var instructions = File.ReadAllText(Path.Combine(assetFolder, "dictionary.instructions.txt"));
        var schema = File.ReadAllText(Path.Combine(assetFolder, "dictionary.schema.json"));
        var translationSchema = File.ReadAllText(Path.Combine(assetFolder, "translation.schema.json"));
        foreach (var arg in new[] { "-p", "--model", settings.Model, "--tools", "", "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}",
            "--setting-sources", "", "--settings", JsonSerializer.Serialize(new ClaudeCliSettings(true, selected.Speed is "fast" or "priority"), Json.Options),
            "--no-chrome", "--no-session-persistence", "--output-format", "stream-json", "--verbose", "--include-partial-messages",
            "--system-prompt", instructions + "\nFor define, use this JSON schema:\n" + schema + "\nFor translate, use this JSON schema:\n" + translationSchema }) info.ArgumentList.Add(arg);
        // Only the official CLI handles subscription credentials. Lexi never reads or copies tokens.
        foreach (var key in ProviderConnection.ClaudeClearedVariables) info.Environment.Remove(key);
        info.Environment.Remove("MAX_THINKING_TOKENS"); info.Environment.Remove("CLAUDE_CODE_EFFORT_LEVEL");
        if (selected.Effort != "") { info.ArgumentList.Add("--effort"); info.ArgumentList.Add(selected.Effort); }
        return new Worker(info, Identity(settings));
    }

    public async Task<AiEntry> LookupAsync(string query, OnlineEntry? online, AppSettings settings, CancellationToken token, IProgress<AiEntry>? progress = null)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var updates = new StreamingPreview<AiEntry>(text => StreamingEntry.Read(text, query), progress, token);
                var result = await RunAsync(CodexDictionary.DefineRequest(query, online) + RetryInstruction(attempt), settings, token, updates.Report);
                return CodexDictionary.ParseAndValidate(StreamingEntry.Unwrap(result), query, online, true);
            }
            catch (Exception ex) when (attempt == 0 && !token.IsCancellationRequested && ex is JsonException or InvalidDataException) { }
        }
    }
    public async Task<TranslationEntry> TranslateAsync(string text, string target, AppSettings settings, CancellationToken token, IProgress<TranslationEntry>? progress = null)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var updates = new StreamingPreview<TranslationEntry>(json => Translation.Preview(json, text, target), progress, token);
                var result = await RunAsync(Translation.Request(text, target) + RetryInstruction(attempt), settings, token, updates.Report);
                return Translation.Parse(result, text, target);
            }
            catch (Exception ex) when (attempt == 0 && !token.IsCancellationRequested && ex is JsonException or InvalidDataException) { }
        }
    }
    private static string RetryInstruction(int attempt) => attempt == 0 ? "" : "\nThe previous response failed structural validation. Regenerate one complete valid JSON object. Escape every embedded double quote and newline inside strings. Follow the requested schema and exact input identity. No commentary or markdown.";
    private async Task<string> RunAsync(string prompt, AppSettings settings, CancellationToken token, Action<string>? preview)
    {
        Worker worker;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var model = Identity(settings); desiredModel = model;
            if (prepared is { IsAlive: true } && prepared.Model == model) { worker = prepared; prepared = null; }
            else { prepared?.Dispose(); prepared = null; worker = Start(settings); }
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
            finally { _ = Task.Run(() => Replenish(settings)); }
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
