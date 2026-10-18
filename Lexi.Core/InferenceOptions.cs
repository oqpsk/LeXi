using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Lexi.Core;

public sealed record InferenceOption(string Id, string Label)
{ public override string ToString() => Label; }
public sealed record InferenceCapabilities(string[] Efforts, InferenceOption[] Speeds, string Note = "")
{
    public InferenceOption[] EffortChoices => [new("", "模型默认"), .. Efforts.Select(id => new InferenceOption(id, InferenceOptions.EffortLabel(id)))];
    public InferenceOption[] SpeedChoices => [new("", "服务默认"), .. Speeds];
}
public sealed record InferenceProfile(string Provider, string Model, string Endpoint, string Effort, string Speed);
public sealed record InferenceSelection(string Effort, string Speed);

// Capability parsing, selections and endpoint scoping are shared by both native settings screens.
public static class InferenceOptions
{
    private static readonly string[] Efforts = ["none", "minimal", "low", "medium", "high", "xhigh", "max", "ultra"];
    private static readonly string[] Speeds = ["auto", "default", "fast", "priority", "ultrafast", "flex", "scale"];
    public static InferenceCapabilities Unknown => new([], [], "此模型未提供可调档位，使用服务默认设置。");
    public static InferenceCapabilities Subscription(string provider, string model)
    {
        // Conservative offline fallback for shipped Codex models. Explicit discovery supersedes it.
        if (provider == "codex" && (model == "" || new[] { "gpt-6-luna", "gpt-6.1-sol", "gpt-6-astra", "gpt-6-sol", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna" }.Contains(model)))
            return new(model.EndsWith("luna", StringComparison.Ordinal) || model == "" ? ["low", "medium", "high", "xhigh", "max"] : ["low", "medium", "high", "xhigh", "max", "ultra"],
                [new("default", "标准"), new("priority", "Fast")], "Fast 可能消耗更多订阅额度；可读取订阅模型更新可选档位。");
        // Aliases depend on the installed Claude Code version; never infer a release from an alias.
        return Unknown;
    }
    // Display names only, written like the speed labels; the id is what reaches the service.
    public static string EffortLabel(string id) => id switch
    { "xhigh" => "Extra High", "" => "", _ => char.ToUpperInvariant(id[0]) + id[1..] };
    public static bool ValidEffort(string value) => value == "" || Efforts.Contains(value);
    public static bool ValidSpeed(string value) => value == "" || Speeds.Contains(value);
    public static string CanonicalSpeed(string value) => value == "fast" ? "priority" : value;
    private static string Scope(AppSettings settings, string provider) => provider == CompatibleText.Provider ? CompatibleText.Scope(settings) : "";
    public static InferenceProfile? Profile(AppSettings settings, LookupModel model) => (settings.InferenceProfiles ?? [])
        .LastOrDefault(p => p != null && p.Provider == model.Provider && p.Model == model.Model && p.Endpoint == Scope(settings, model.Provider));
    public static InferenceSelection Selected(AppSettings settings, LookupModel model)
    {
        if (Profile(settings, model) is { } p) return new(p.Effort, CanonicalSpeed(p.Speed));
        // Preserve old Codex preferences. The compatible client previously sent neither parameter.
        return model.Provider == "codex" ? new(settings.ReasoningEffort, CanonicalSpeed(settings.ServiceTier)) : new("", "");
    }
    public static string? ValidateSelection(InferenceCapabilities capabilities, string effort, string speed)
    {
        if (!ValidEffort(effort) || effort != "" && !capabilities.Efforts.Contains(effort)) return "此模型不支持所选 effort，请重新选择。";
        if (!ValidSpeed(speed) || speed != "" && !capabilities.Speeds.Any(s => CanonicalSpeed(s.Id) == CanonicalSpeed(speed))) return "此模型不支持所选速度，请重新选择。";
        return null;
    }
    public static void Configure(AppSettings settings, ModelChoice model, string effort, string speed)
    {
        if (ValidateSelection(model.Capabilities, effort, speed) is { } error) throw new InvalidOperationException(error);
        var scope = Scope(settings, model.Provider);
        settings.InferenceProfiles = [.. (settings.InferenceProfiles ?? []).Where(p => p.Provider != model.Provider || p.Model != model.Id || p.Endpoint != scope),
            new(model.Provider, model.Id, scope, effort, CanonicalSpeed(speed))];
    }
    public static string Identity(AppSettings settings, LookupModel model) => Profile(settings, model) is { } p
        ? JsonSerializer.Serialize(new { p.Effort, speed = CanonicalSpeed(p.Speed) }, Json.Options) : "";
    public static string EnvelopeEffort(AppSettings settings, LookupModel model) => Profile(settings, model)?.Effort ?? settings.ReasoningEffort;
    public static void ValidateRequest(AppSettings settings, LookupModel model, InferenceCapabilities capabilities)
    {
        var selected = Selected(settings, model);
        if (ValidateSelection(capabilities, selected.Effort, selected.Speed) is { } error) throw new InvalidOperationException(error + " 请在设置中更新模型列表。");
    }
    private static string String(JsonElement item, params string[] names)
    {
        if (item.ValueKind == JsonValueKind.String) return item.GetString() ?? "";
        if (item.ValueKind == JsonValueKind.Object) foreach (var name in names)
            if (item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String) return value.GetString() ?? "";
        return "";
    }
    public static InferenceCapabilities Parse(JsonElement model)
    {
        var efforts = new List<string>(); var speeds = new List<InferenceOption>();
        foreach (var name in new[] { "supported_reasoning_levels", "supportedReasoningEfforts", "supportedEffortLevels", "reasoning_efforts" })
            if (model.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array)
                efforts.AddRange(values.EnumerateArray().Select(v => String(v, "effort", "reasoningEffort", "id")).Where(v => v != "" && Efforts.Contains(v)));
        foreach (var name in new[] { "service_tiers", "serviceTiers" })
            if (model.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array)
                foreach (var value in values.EnumerateArray())
                {
                    var id = String(value, "id"); if (!Speeds.Contains(id)) continue;
                    var label = id switch { "priority" or "fast" => "Fast", "ultrafast" => "Ultrafast", "default" => "标准", "auto" => "自动", "flex" => "Flex", _ => id };
                    speeds.Add(new(CanonicalSpeed(id), label));
                }
        if (model.TryGetProperty("supportsFastMode", out var fast) && fast.ValueKind == JsonValueKind.True) speeds.Add(new("priority", "Fast"));
        if (speeds.Count > 0 && !speeds.Any(s => s.Id == "default")) speeds.Insert(0, new("default", "标准"));
        var e = efforts.Distinct().ToArray(); var s = speeds.DistinctBy(s => s.Id).ToArray();
        return new(e, s, e.Length == 0 && s.Length == 0 ? Unknown.Note : "按服务端能力提供选项；加速档位可能消耗更多额度。");
    }
    private static string MetadataKey(string provider, string endpoint) => provider == CompatibleText.Provider ? CompatibleText.NormalizeBase(endpoint) : provider;
    public static ModelChoice[] Load(string root, string provider, string endpoint = "")
    {
        try
        {
            var items = new Cache(root).Read<ModelChoice[]>("model-capabilities-v1", MetadataKey(provider, endpoint)) ?? [];
            return items.Length <= 10000 ? items.Where(m => m != null && m.Provider == provider && (CompatibleText.ValidModel(m.Id) || provider == "codex" && m.Id == "") &&
                m.Options != null && m.Options.Efforts != null && m.Options.Speeds != null && m.Options.Efforts.Length <= 32 && m.Options.Speeds.Length <= 32 && m.Options.Efforts.All(ValidEffort) && m.Options.Speeds.All(s => s != null && ValidSpeed(s.Id))).ToArray() : [];
        }
        catch (CompatibleException) { return []; }
    }
    public static Task Remember(string root, string provider, string endpoint, ModelChoice[] models, CancellationToken token = default) =>
        new Cache(root).WriteAsync("model-capabilities-v1", MetadataKey(provider, endpoint), models, token);
    public static ModelChoice[] Available(string root, AppSettings settings) => ModelCatalog.Choices(settings, details:
        Load(root, "codex").Concat(Load(root, "claude")).Concat(Load(root, CompatibleText.Provider, settings.CompatibleBaseUrl)));
    public static void ValidateRequest(AppSettings settings, string root)
    {
        var model = LookupModel.Primary(settings);
        var choice = Available(root, settings).First(m => m.Provider == model.Provider && m.Id == model.Model);
        ValidateRequest(settings, model, choice.Capabilities);
    }
    // Official app-server metadata only. No account/token/configuration file is read by LeXi.
    public static async Task<ModelChoice[]> ReadSubscriptions(string configuredPath, CancellationToken token)
    {
        var tasks = new List<Task<ModelChoice[]>>();
        if (ToolLocator.Codex(configuredPath) != null) tasks.Add(ReadCodexSubscriptions(configuredPath, token));
        if (ToolLocator.Claude() != null) tasks.Add(ReadClaudeSubscriptions(token));
        if (tasks.Count == 0) throw new InvalidOperationException("未找到官方模型客户端，请先安装并登录。");
        async Task<ModelChoice[]> Read(Task<ModelChoice[]> task)
        {
            try { return await task; }
            catch (Exception e) when (e is InvalidOperationException or TimeoutException or IOException or System.ComponentModel.Win32Exception) { return []; }
        }
        var models = (await Task.WhenAll(tasks.Select(Read))).SelectMany(m => m).ToArray();
        token.ThrowIfCancellationRequested();
        return models.Length > 0 ? models : throw new InvalidOperationException("无法读取订阅模型，请检查官方客户端版本与登录状态。");
    }
    public static async Task RememberSubscriptions(string root, ModelChoice[] models, CancellationToken token)
    {
        foreach (var group in models.GroupBy(m => m.Provider)) await Remember(root, group.Key, "", group.ToArray(), token);
    }
    private static async Task<ModelChoice[]> ReadClaudeSubscriptions(CancellationToken token)
    {
        var tool = ToolLocator.Claude() ?? throw new InvalidOperationException("未找到 Claude Code。");
        var info = tool.StartInfo(); info.UseShellExecute = false; info.CreateNoWindow = true;
        info.RedirectStandardInput = true; info.RedirectStandardOutput = true; info.RedirectStandardError = true;
        info.StandardInputEncoding = new UTF8Encoding(false); info.StandardOutputEncoding = Encoding.UTF8;
        foreach (var arg in new[] { "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose", "--tools", "",
            "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}", "--setting-sources", "", "--settings", "{\"disableAllHooks\":true}", "--no-chrome", "--no-session-persistence" }) info.ArgumentList.Add(arg);
        foreach (var key in ProviderConnection.ClaudeClearedVariables) info.Environment.Remove(key);
        using var process = new Process { StartInfo = info };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        token.ThrowIfCancellationRequested(); process.Start();
        using var stopped = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var errors = Task.Run(async () => { try { while (await process.StandardError.ReadLineAsync(timeout.Token) != null) { } } catch (OperationCanceledException) { } });
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { type = "control_request", request_id = "lexi-models", request = new { subtype = "initialize" } }, Json.Options).AsMemory(), timeout.Token);
            await process.StandardInput.FlushAsync(timeout.Token);
            for (int count = 0; count < 100; count++)
            {
                var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                if (line == null || line.Length > 2000000) break;
                using var data = JsonDocument.Parse(line);
                var message = data.RootElement;
                if (String(message, "type") != "control_response" || !message.TryGetProperty("response", out var response) ||
                    String(response, "request_id") != "lexi-models") continue;
                if (!response.TryGetProperty("response", out var result) || !result.TryGetProperty("models", out var list) || list.ValueKind != JsonValueKind.Array) break;
                return list.EnumerateArray().Where(m => CompatibleText.ValidModel(String(m, "value"))).Select(m => ModelCatalog.Choice("claude", String(m, "value"), Parse(m))).ToArray();
            }
            throw new InvalidOperationException("无法读取 Claude 订阅模型列表，请更新官方客户端后重试。");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("读取 Claude 订阅模型超时。"); }
        catch (JsonException) { throw new InvalidOperationException("Claude 客户端未返回有效模型列表。"); }
        finally { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } try { await errors; } catch (IOException) { } }
    }
    private static async Task<ModelChoice[]> ReadCodexSubscriptions(string configuredPath, CancellationToken token)
    {
        var tool = ToolLocator.Codex(configuredPath) ?? throw new ModelAccessException(ModelAccessFailure.ClientMissing);
        var info = tool.StartInfo(); info.UseShellExecute = false; info.CreateNoWindow = true;
        info.RedirectStandardInput = true; info.RedirectStandardOutput = true; info.RedirectStandardError = true;
        info.StandardInputEncoding = new UTF8Encoding(false); info.StandardOutputEncoding = Encoding.UTF8;
        info.ArgumentList.Add("app-server"); info.ArgumentList.Add("--stdio");
        using var process = new Process { StartInfo = info };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
        token.ThrowIfCancellationRequested(); process.Start();
        using var stopped = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var errors = Task.Run(async () => { try { while (await process.StandardError.ReadLineAsync(timeout.Token) != null) { } } catch (OperationCanceledException) { } });
        async Task Send(object value) { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value, Json.Options).AsMemory(), timeout.Token); await process.StandardInput.FlushAsync(timeout.Token); }
        async Task<JsonDocument> Response(int expected)
        {
            for (int count = 0; count < 100; count++)
            {
                var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                if (line == null || line.Length > 2000000) break;
                var data = JsonDocument.Parse(line);
                if (data.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == expected) return data;
                data.Dispose();
            }
            throw new InvalidOperationException("无法读取订阅模型列表，请检查 Codex 客户端版本与登录状态。");
        }
        try
        {
            await Send(new { id = 1, method = "initialize", @params = new { clientInfo = new { name = "LeXi", version = "1" } } });
            using var ready = await Response(1);
            await Send(new { method = "initialized" });
            var models = new List<ModelChoice>(); string? cursor = null;
            for (int page = 0; page < 20; page++)
            {
                await Send(new { id = 2, method = "model/list", @params = new { cursor, includeHidden = true } });
                using var data = await Response(2);
                if (!data.RootElement.TryGetProperty("result", out var result) || !result.TryGetProperty("data", out var list) || list.ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException("无法读取订阅模型列表，请检查 Codex 客户端版本与登录状态。");
                foreach (var model in list.EnumerateArray())
                {
                    var id = String(model, "model", "id"); if (!CompatibleText.ValidModel(id)) continue;
                    models.Add(ModelCatalog.Choice("codex", id, Parse(model), model.TryGetProperty("hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True));
                }
                cursor = result.TryGetProperty("nextCursor", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
                if (string.IsNullOrEmpty(cursor)) return models.DistinctBy(m => m.Id).ToArray();
            }
            throw new InvalidOperationException("订阅模型列表过长，请更新客户端后重试。");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("读取订阅模型超时，请稍后重试。"); }
        catch (JsonException) { throw new InvalidOperationException("订阅客户端返回了无效的模型列表，请更新客户端后重试。"); }
        finally { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } try { await errors; } catch (IOException) { } }
    }
}
