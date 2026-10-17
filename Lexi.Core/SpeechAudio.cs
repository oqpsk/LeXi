namespace Lexi.Core;

// Synthesized speech files shared by every front end. Each text, provider, model and voice has its
// own file in <user>/speech-cache (SpeechOptions.CacheKey), and a file appears there only once it
// is complete. Online audio is generated here; local synthesis is supplied by the front end.
public sealed class SpeechAudio(string userRoot, SpeechClient? client = null) : IDisposable
{
    public const string StorageFailure = "发音缓存无法写入，请检查用户数据文件夹的权限和磁盘空间。";
    private readonly SpeechClient client = client ?? new();
    private readonly bool owned = client == null;

    public string PathFor(string text, SpeechOptions options) => Path.Combine(userRoot, "speech-cache",
        options.CacheKey(text) + (SpeechCatalog.IsLocal(options.Provider) ? ".wav" : ".mp3"));

    // The complete file for this text and voice, or null. Never generates audio.
    public string? Cached(string text, SpeechOptions options)
    {
        SpeechCatalog.Validate(text, options);
        var path = PathFor(text, options);
        return File.Exists(path) ? path : null;
    }

    // The key is read only when the audio still has to be generated.
    public async Task<string> OnlineAsync(string text, SpeechOptions options, bool online, Func<string> key, Action<string>? status, CancellationToken token)
    {
        if (SpeechCatalog.IsLocal(options.Provider)) throw new InvalidOperationException("此模型不使用在线语音接口。");
        return await PrepareAsync(text, options, online, async (temp, cancel) =>
            await File.WriteAllBytesAsync(temp, await client.GenerateAsync(text, options, key(), cancel), cancel), status, token);
    }

    // generate writes the audio to the temporary path it is given; only a finished file is kept.
    public async Task<string> PrepareAsync(string text, SpeechOptions options, bool online, Func<string, CancellationToken, Task> generate,
        Action<string>? status, CancellationToken token)
    {
        if (Cached(text, options) is { } cached) return cached;
        if (!SpeechCatalog.IsLocal(options.Provider) && !online) throw new InvalidOperationException("离线模式下没有此声线的缓存发音。");
        status?.Invoke("正在生成发音…");
        var path = PathFor(text, options);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await generate(temp, token);
            token.ThrowIfCancellationRequested(); File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return path;
    }

    // Account voices are read only in online mode, with the key the user supplied.
    public async Task<SpeechVoice[]> ElevenVoicesAsync(bool online, string key, CancellationToken token)
    {
        if (!online) throw new InvalidOperationException("请先开启在线补充，再读取声线列表。");
        return await client.ElevenVoicesAsync(key, token);
    }

    public void Dispose() { if (owned) client.Dispose(); }
}
