using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Lexi.Core;

public sealed record SpeechModel(string Provider, string Id, string Name)
{ public override string ToString() => Name; }
public sealed record SpeechVoice(string Id, string Name)
{ public override string ToString() => Name; }
public sealed record SpeechOptions(string Provider, string Model, string Voice)
{
    public string CacheKey(string text) => Cache.Hash(JsonSerializer.Serialize(new { version = 1, text, Provider, Model, Voice }, Json.Options));
    public static SpeechOptions From(AppSettings settings) => new(settings.SpeechProvider, settings.SpeechModel, settings.SpeechVoice);
}

public static class SpeechCatalog
{
    public static SpeechModel[] Models => [new("windows", "sapi", "Windows 本地发音"), new("openai", "gpt-4o-mini-tts", "OpenAI · GPT-4o mini TTS"),
        new("openai", "tts-1", "OpenAI · TTS-1"), new("openai", "tts-1-hd", "OpenAI · TTS-1 HD"), new("xai", "grok-tts", "Grok · TTS")];
    public static SpeechVoice[] Voices(string provider, string model)
    {
        var ids = provider == "xai" ? new[] { "eve", "ara", "leo", "rex", "sal" } :
            model == "gpt-4o-mini-tts" ? ["coral", "alloy", "ash", "ballad", "echo", "fable", "nova", "onyx", "sage", "shimmer", "verse", "marin", "cedar"] :
            new[] { "alloy", "ash", "coral", "echo", "fable", "onyx", "nova", "sage", "shimmer" };
        return ids.Select(id => new SpeechVoice(id, char.ToUpperInvariant(id[0]) + id[1..])).ToArray();
    }
    public static void Validate(string text, SpeechOptions options)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("没有可朗读的文字。");
        var max = options.Provider == "openai" ? 4096 : 5000;
        if (text.Length > max) throw new InvalidOperationException($"此发音服务每次最多朗读 {max} 个字符。");
        if (!Models.Any(m => m.Provider == options.Provider && m.Id == options.Model)) throw new InvalidOperationException("请在设置中重新选择发声模型。");
        if (options.Provider != "windows" && !Voices(options.Provider, options.Model).Any(v => v.Id == options.Voice))
            throw new InvalidOperationException("请在设置中选择这个模型支持的声线。");
    }
}

public sealed class SpeechClient : IDisposable
{
    private readonly HttpClient client;
    public SpeechClient(HttpMessageHandler? handler = null)
    { client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }); client.Timeout = TimeSpan.FromSeconds(35); }
    public static HttpRequestMessage Request(string text, SpeechOptions options, string apiKey)
    {
        SpeechCatalog.Validate(text, options);
        if (options.Provider is not ("openai" or "xai")) throw new InvalidOperationException("此模型不使用在线语音接口。");
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("请在设置 → 发音中填写所选服务的 API Key。");
        if (apiKey.Trim().Any(char.IsWhiteSpace) || apiKey.Length > 2048) throw new InvalidOperationException("API Key 格式无效，请检查是否包含换行或空格。");
        object body = options.Provider == "openai" ? new { model = options.Model, voice = options.Voice, input = text, response_format = "mp3" } :
            new { text, voice_id = options.Voice, language = "auto", output_format = new { codec = "mp3", sample_rate = 24000, bit_rate = 128000 } };
        var request = new HttpRequestMessage(HttpMethod.Post, options.Provider == "openai" ? "https://api.openai.com/v1/audio/speech" : "https://api.x.ai/v1/tts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Content = new StringContent(JsonSerializer.Serialize(body, Json.Options), Encoding.UTF8, "application/json");
        return request;
    }
    public async Task<byte[]> GenerateAsync(string text, SpeechOptions options, string apiKey, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(35));
        try { return await ReceiveAsync(text, options, apiKey, timeout.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("语音生成超时，请重试。"); }
    }
    private async Task<byte[]> ReceiveAsync(string text, SpeechOptions options, string apiKey, CancellationToken token)
    {
        using var request = Request(text, options, apiKey);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "语音服务拒绝认证，请检查 API Key 和接口权限。",
                HttpStatusCode.TooManyRequests => "语音服务额度不足或请求过多，请稍后重试。",
                _ => $"语音服务暂不可用（HTTP {(int)response.StatusCode}）。"
            });
        const int limit = 8 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("语音文件过大，已停止接收。");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream(); var buffer = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        { if (output.Length + read > limit) throw new InvalidDataException("语音文件过大，已停止接收。"); await output.WriteAsync(buffer.AsMemory(0, read), token); }
        var bytes = output.ToArray();
        if (!IsMp3(bytes)) throw new InvalidDataException("语音服务没有返回有效的 MP3 音频。");
        return bytes;
    }
    public static bool IsMp3(byte[] bytes) => bytes.Length > 16 &&
        (bytes[0] == 'I' && bytes[1] == 'D' && bytes[2] == '3' || bytes[0] == 0xff && (bytes[1] & 0xe0) == 0xe0);
    public void Dispose() => client.Dispose();
}
