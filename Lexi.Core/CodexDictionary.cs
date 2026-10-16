using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Lexi.Core;

public sealed class CodexDictionary(string assetFolder, string userRoot)
{
    public static string? FindExecutable(string configured = "")
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var path = Path.Combine(directory.Trim('"'), "codex.exe");
            if (File.Exists(path)) return path;
        }
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateDirectories(root).OrderByDescending(Directory.GetLastWriteTimeUtc)
            .Select(x => Path.Combine(x, "codex.exe")).FirstOrDefault(File.Exists);
    }
    public static string EvidenceText(OnlineEntry? online) => online == null ? "No live dictionary evidence available." :
        string.Join("\n", online.Senses.Take(20).Select((s, i) => $"[{i + 1}] {s.PartOfSpeech}: {s.Definition}"));
    public static string EvidenceHash(OnlineEntry? online) => Cache.Hash(EvidenceText(online));
    public static AiEntry ParseAndValidate(string text, string query, OnlineEntry? online)
    {
        var entry = JsonSerializer.Deserialize<AiEntry>(text, Json.Options) ?? throw new InvalidDataException("Codex 未返回释义。");
        if (Queries.Normalize(entry.Word) != Queries.Normalize(query) || entry.Senses == null)
            throw new InvalidDataException("Codex 返回了不匹配的词条。");
        if (!entry.Found || entry.Senses.Length == 0) return entry with { Found = false, Senses = [], EvidenceStatus = "unknown" };
        if (entry.Senses.Length > 8) throw new InvalidDataException("Codex 返回的词条过长。");
        foreach (var s in entry.Senses)
        {
            if (string.IsNullOrWhiteSpace(s.English) || string.IsNullOrWhiteSpace(s.Chinese) || s.Evidence == null ||
                s.Evidence.Any(n => n < 1 || n > (online?.Senses.Length ?? 0)))
                throw new InvalidDataException("Codex 返回的释义或来源编号无效。");
        }
        // Source-based is a provenance label, never a claim that AI correctness was verified.
        var supported = online != null && entry.EvidenceStatus == "source_based" && entry.Senses.All(s => s.Evidence.Length > 0);
        return entry with { EvidenceStatus = supported ? "source_based" : "unverified" };
    }
    public async Task<AiEntry> LookupAsync(string query, OnlineEntry? online, AppSettings settings, CancellationToken token)
    {
        var exe = FindExecutable(settings.CodexPath) ?? throw new InvalidOperationException("未找到 Codex。请在设置中指定 codex.exe。");
        var dir = Path.Combine(userRoot, "codex-work");
        Directory.CreateDirectory(dir);
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = dir };
        foreach (var arg in new[] { "exec", "--ignore-user-config", "--ephemeral", "--skip-git-repo-check", "--sandbox", "read-only", "--json",
            "--color", "never", "--output-schema", Path.Combine(assetFolder, "dictionary.schema.json") }) info.ArgumentList.Add(arg);
        foreach (var config in new[] { "approval_policy=\"never\"", "web_search=\"disabled\"", "project_doc_max_bytes=0", "model_reasoning_effort=" + JsonSerializer.Serialize(settings.ReasoningEffort),
            "service_tier=" + JsonSerializer.Serialize(settings.ServiceTier), "features.fast_mode=true",
            "features.shell_tool=false", "features.apps=false", "features.plugins=false", "features.memories=false", "features.multi_agent=false",
            "features.computer_use=false", "features.browser_use=false", "features.skip_host_skill_discovery=true",
            "model_instructions_file=" + JsonSerializer.Serialize(Path.Combine(assetFolder, "dictionary.instructions.txt")) })
        { info.ArgumentList.Add("-c"); info.ArgumentList.Add(config); }
        if (!string.IsNullOrWhiteSpace(settings.Model)) { info.ArgumentList.Add("--model"); info.ArgumentList.Add(settings.Model.Trim()); }
        info.ArgumentList.Add("-");
        // The CLI manages OAuth itself. No access token, refresh token, or auth file is read by Lexi.
        using var process = new Process { StartInfo = info };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(70));
        token.ThrowIfCancellationRequested();
        process.Start();
        using var registration = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        var prompt = "Return a bilingual dictionary entry for this exact query. Focus on current usage; use the numbered evidence when available.\n" +
            JsonSerializer.Serialize(new { query, source = online?.Url, evidence = EvidenceText(online) }, Json.Options);
        string? final = null;
        string? failure = null;
        try
        {
            await process.StandardInput.WriteLineAsync(prompt.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                if (line.Length > 250000) throw new InvalidDataException("Codex 返回内容过长。");
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (root.TryGetProperty("item", out var item))
                {
                    var itemType = item.GetProperty("type").GetString();
                    if (itemType is "command_execution" or "mcp_tool_call" or "file_change" or "web_search")
                    { process.Kill(true); throw new InvalidDataException("词典查询意外请求了额外工具，已停止本次调用。"); }
                    if (type == "item.completed" && itemType == "agent_message") final = item.GetProperty("text").GetString();
                }
                if (type == "turn.failed") failure = "Codex 查询失败。请检查登录状态或账户额度。";
            }
            await process.WaitForExitAsync(timeout.Token);
            var error = await errorTask;
            timeout.Token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0 || failure != null)
            {
                if (error.Contains("401") || error.Contains("not logged", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Codex 登录已失效，请在 Codex 中重新登录。");
                throw new InvalidOperationException(failure ?? "Codex 暂未完成查询。请检查登录、网络或模型名称。");
            }
            return ParseAndValidate(final ?? throw new InvalidDataException("Codex 未返回可读取的释义。"), query, online);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Codex 查询超过 70 秒，已取消。可以稍后重试。"); }
        finally
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            try { await errorTask; } catch (Exception ex) when (ex is OperationCanceledException or IOException) { }
        }
    }
}
