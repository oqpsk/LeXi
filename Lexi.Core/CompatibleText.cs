using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lexi.Core;

// Only non-secret connection settings belong in AppSettings. Credentials are supplied by the
// front end's protected store and never serialized as settings, provenance or diagnostic data.
public sealed class CompatibleCredentials(string clientId = "", string clientSecret = "", string apiKey = "")
{
    [JsonIgnore] public string ClientId { get; } = clientId.Trim();
    [JsonIgnore] public string ClientSecret { get; } = clientSecret.Trim();
    [JsonIgnore] public string ApiKey { get; } = apiKey.Trim();
    public override string ToString() => "[protected text credentials]";
}

public sealed class CompatibleException(string code, string message) : InvalidOperationException(message)
{ public string Code { get; } = code; }

public static class CompatibleText
{
    public const string Provider = "compatible";
    public const string DefaultBaseUrl = "https://ai.rwby.io/v1";
    public const string MissingCredentials = "请在设置 → 自定义文本服务中填写 CF Access Client ID 和 Client Secret。";
    public const string AuthenticationFailed = "自定义文本服务鉴权失败（401/403），请检查 CF Access Client ID、Client Secret 及可选 API Key。";
    public const string StorageFailure = "自定义文本服务凭据无法读取或保存，请在设置中重新填写。";
    public static string UserAgent => "LeXi/" + (typeof(CompatibleText).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
    public static string NormalizeBase(string value)
    {
        if (!Uri.TryCreate((value ?? "").Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new CompatibleException("invalid_endpoint", "Base URL 必须是 HTTPS 地址，且不含账号、查询参数或片段。请将凭据填写到专用输入框。");
        return uri.AbsoluteUri.TrimEnd('/');
    }
    public static Uri Url(string value, string resource) => new(NormalizeBase(value) + "/" + resource);
    // Cache identities remain safe to calculate even if somebody hand-edits settings to an invalid URL.
    public static string Scope(AppSettings settings)
    {
        string value;
        try { value = NormalizeBase(settings.CompatibleBaseUrl); }
        catch (CompatibleException) { value = (settings.CompatibleBaseUrl ?? "").Trim(); }
        return Cache.Hash(value);
    }
    public static bool ValidModel(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl);
    public static void ValidateCredentials(CompatibleCredentials credentials)
    {
        if (credentials.ClientId.Length == 0 || credentials.ClientSecret.Length == 0)
            throw new CompatibleException("missing_credentials", MissingCredentials);
        if (new[] { credentials.ClientId, credentials.ClientSecret, credentials.ApiKey }.Any(v => v.Length > 4096 || v.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))))
            throw new CompatibleException("invalid_credentials", "凭据格式无效，请检查是否混入空格或换行。");
    }
}

// A direct Chat Completions client. No CLI, OAuth store, provider fallback, tools or audio routes.
public sealed class CompatibleTextClient : IDisposable
{
    private readonly HttpClient http;
    public CompatibleTextClient(HttpMessageHandler? handler = null)
    {
        http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        http.Timeout = Timeout.InfiniteTimeSpan;
    }
    private static HttpRequestMessage Request(HttpMethod method, string baseUrl, string resource, CompatibleCredentials credentials)
    {
        CompatibleText.ValidateCredentials(credentials);
        var request = new HttpRequestMessage(method, CompatibleText.Url(baseUrl, resource));
        request.Headers.Add("CF-Access-Client-Id", credentials.ClientId);
        request.Headers.Add("CF-Access-Client-Secret", credentials.ClientSecret);
        request.Headers.UserAgent.ParseAdd(CompatibleText.UserAgent);
        if (credentials.ApiKey.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.ApiKey);
        return request;
    }
    public Task<string[]> ModelsAsync(string baseUrl, CompatibleCredentials credentials, CancellationToken token) => Guard(async cancel =>
    {
        using var request = Request(HttpMethod.Get, baseUrl, "models", credentials);
        request.Headers.Accept.Add(new("application/json"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        CheckStatus(response);
        using var json = JsonDocument.Parse(await ReadJsonAsync(response, cancel));
        if (!json.RootElement.TryGetProperty("data", out var models) || models.ValueKind != JsonValueKind.Array)
            throw Protocol();
        var ids = models.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()! : "").ToArray();
        if (ids.Length > 10000 || ids.Any(id => !CompatibleText.ValidModel(id))) throw Protocol();
        return ids.Distinct(StringComparer.Ordinal).ToArray();
    }, token);

    public Task<string> CompleteAsync(string baseUrl, string model, bool streaming, CompatibleCredentials credentials,
        string instructions, string prompt, CancellationToken token, Action<string>? preview = null) => Guard(async cancel =>
    {
        if (!CompatibleText.ValidModel(model)) throw new CompatibleException("missing_model", "请先连接自定义文本服务，读取模型列表并选择模型。");
        using var request = Request(HttpMethod.Post, baseUrl, "chat/completions", credentials);
        request.Headers.Accept.Add(new(streaming ? "text/event-stream" : "application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model, messages = new[] { new { role = "system", content = instructions }, new { role = "user", content = prompt } }, stream = streaming
        }, Json.Options), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        CheckStatus(response);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (string.Equals(contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            return await ReadEventsAsync(response, preview, cancel);
        // Some compatible services return a complete JSON object even when streaming was requested.
        using var json = JsonDocument.Parse(await ReadJsonAsync(response, cancel));
        var choice = Choice(json.RootElement);
        CheckFinish(choice);
        if (!choice.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String) throw Protocol();
        RejectTools(message);
        var result = content.GetString()!;
        if (result.Length is 0 or > 250000) throw Protocol();
        preview?.Invoke(result);
        return result;
    }, token);

    private static void CheckStatus(HttpResponseMessage response)
    {
        // Never reflect a provider body, redirect location, reason phrase or exception message.
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden || (int)response.StatusCode is >= 300 and <= 399)
            throw new CompatibleException("authentication_failed", CompatibleText.AuthenticationFailed);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new CompatibleException("rate_limited", "自定义文本服务请求过多或额度不足，请稍后重试。");
        if (!response.IsSuccessStatusCode)
            throw new CompatibleException("service_failed", $"自定义文本服务请求失败（HTTP {(int)response.StatusCode}），请检查模型与服务配置。");
    }
    private static CompatibleException Protocol() => new("invalid_response", "自定义文本服务返回了无效或不完整的文本响应，请检查接口协议与模型后重试。");
    private static JsonElement Choice(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("error", out _) ||
            !root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array) throw Protocol();
        foreach (var choice in choices.EnumerateArray())
            if (choice.ValueKind == JsonValueKind.Object && (!choice.TryGetProperty("index", out var index) || index.ValueKind == JsonValueKind.Number && index.TryGetInt32(out var number) && number == 0)) return choice;
        throw Protocol();
    }
    private static bool CheckFinish(JsonElement choice)
    {
        if (!choice.TryGetProperty("finish_reason", out var finish) || finish.ValueKind == JsonValueKind.Null) return false;
        if (finish.ValueKind != JsonValueKind.String || finish.GetString() != "stop")
            throw new CompatibleException("incomplete_response", "自定义文本服务未完成生成，已保留现有结果；请重试或检查服务端输出长度限制。");
        return true;
    }
    private static void RejectTools(JsonElement message)
    {
        if (message.TryGetProperty("tool_calls", out var tools) && tools.ValueKind != JsonValueKind.Null ||
            message.TryGetProperty("function_call", out var function) && function.ValueKind != JsonValueKind.Null ||
            message.TryGetProperty("refusal", out var refusal) && refusal.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(refusal.GetString())) throw Protocol();
    }
    private static async Task<string> ReadJsonAsync(HttpResponseMessage response, CancellationToken token)
    {
        using var stream = await response.Content.ReadAsStreamAsync(token);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        var text = new StringBuilder(); var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        { if (text.Length + read > 2000000) throw Protocol(); text.Append(buffer, 0, read); }
        return text.ToString();
    }
    private static async IAsyncEnumerable<string> LinesAsync(Stream stream, [EnumeratorCancellation] CancellationToken token)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 4096, true);
        var line = new StringBuilder(); var buffer = new char[2048]; int total = 0, read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            total += read; if (total > 8000000) throw Protocol();
            for (int i = 0; i < read; i++)
                if (buffer[i] == '\n') { yield return line.ToString().TrimEnd('\r'); line.Clear(); }
                else { line.Append(buffer[i]); if (line.Length > 300000) throw Protocol(); }
        }
        if (line.Length > 0) yield return line.ToString().TrimEnd('\r');
    }
    private static async Task<string> ReadEventsAsync(HttpResponseMessage response, Action<string>? preview, CancellationToken token)
    {
        using var stream = await response.Content.ReadAsStreamAsync(token);
        var text = new StringBuilder(); var data = new StringBuilder(); bool done = false, finished = false;
        void Frame()
        {
            if (data.Length == 0) return;
            var payload = data.ToString().TrimEnd('\n'); data.Clear();
            if (payload.Trim() == "[DONE]") { done = true; return; }
            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("error", out _)) throw Protocol();
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() == 0) return;
            var choice = Choice(root); finished |= CheckFinish(choice);
            if (!choice.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object) throw Protocol();
            RejectTools(delta);
            if (!delta.TryGetProperty("content", out var content) || content.ValueKind == JsonValueKind.Null) return;
            if (content.ValueKind != JsonValueKind.String) throw Protocol();
            text.Append(content.GetString()); if (text.Length > 250000) throw Protocol();
            token.ThrowIfCancellationRequested(); preview?.Invoke(text.ToString());
        }
        await foreach (var line in LinesAsync(stream, token))
        {
            if (line.Length == 0) { Frame(); if (done) break; }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var value = line.AsSpan(5); if (value.StartsWith(" ")) value = value[1..];
                data.Append(value); data.Append('\n'); if (data.Length > 300000) throw Protocol();
            }
        }
        if (!done) Frame();
        token.ThrowIfCancellationRequested();
        if ((!done && !finished) || text.Length == 0) throw Protocol();
        return text.ToString();
    }
    private static async Task<T> Guard<T>(Func<CancellationToken, Task<T>> run, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(75));
        try { return await run(timeout.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new CompatibleException("timeout", "自定义文本服务响应超时，已中止请求，请重试。"); }
        catch (HttpRequestException) { throw new CompatibleException("connection_failed", "无法连接自定义文本服务，请检查网络和 Base URL 后重试。"); }
        catch (Exception e) when (e is IOException or JsonException or DecoderFallbackException) { throw Protocol(); }
    }
    public void Dispose() => http.Dispose();
}
