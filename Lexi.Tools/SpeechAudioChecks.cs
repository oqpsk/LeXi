using System.Net;
using System.Text;
using Lexi.Core;

// The speech cache shared by Windows and the macOS host: file naming, the offline rule, complete
// writes and lazy keys. Every transport is synthetic.
internal static class SpeechAudioChecks
{
    public static async Task Run()
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
        var root = Path.Combine(Path.GetTempPath(), "lexi-speech-checks-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "speech-cache");
        var audio = new byte[32]; Encoding.ASCII.GetBytes("ID3").CopyTo(audio, 0);
        var openai = new SpeechOptions("openai", "gpt-4o-mini-tts", "coral");
        try
        {
            Check(SpeechCatalog.Local.Name == (OperatingSystem.IsMacOS() ? "macOS 本地发音" : "Windows 本地发音") && SpeechCatalog.Models[0] == SpeechCatalog.Local &&
                SpeechCatalog.Voices(SpeechCatalog.Local.Provider, SpeechCatalog.Local.Id).Length == 0,
                "the local model names this platform's system voices, which the front end lists itself");
            SpeechCatalog.Validate("hello", new(SpeechCatalog.Local.Provider, SpeechCatalog.Local.Id, "any-system-voice-id"));
            Check(true, "local voices are validated by the operating system, not the online catalog");

            int requests = 0, keyReads = 0;
            var statuses = new List<string>();
            string Key() { keyReads++; return "synthetic"; }
            using (var speech = new SpeechAudio(root, new SpeechClient(new Handler((_, _) =>
                { requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(audio) }); }))))
            {
                Check(speech.Cached("word", openai) == null && requests == 0 && keyReads == 0, "looking up the cache never generates audio or reads a key");
                var path = await speech.OnlineAsync("word", openai, true, Key, statuses.Add, CancellationToken.None);
                Check(path == Path.Combine(folder, openai.CacheKey("word") + ".mp3") && File.ReadAllBytes(path).SequenceEqual(audio) &&
                    requests == 1 && keyReads == 1 && statuses.SequenceEqual(["正在生成发音…"]),
                    "online audio is generated once into the provider/model/voice cache file");
                Check(await speech.OnlineAsync("word", openai, false, () => throw new Exception("key read"), statuses.Add, CancellationToken.None) == path &&
                    speech.Cached("word", openai) == path && requests == 1 && statuses.Count == 1,
                    "cached audio replays offline without a request, a key or a generation status");
                var voices = new[] { openai with { Voice = "alloy" }, openai with { Model = "tts-1" }, new SpeechOptions("xai", "grok-tts", "eve") };
                foreach (var choice in voices) await speech.OnlineAsync("word", choice, true, Key, null, CancellationToken.None);
                await speech.OnlineAsync("Word", openai, true, Key, null, CancellationToken.None);
                Check(requests == 5 && Directory.GetFiles(folder).Length == 5, "provider, model, voice and case each keep their own audio file");
                try { await speech.OnlineAsync("new", openai, false, Key, statuses.Add, CancellationToken.None); Check(false, "offline generation"); }
                catch (InvalidOperationException e)
                {
                    Check(e.Message == "离线模式下没有此声线的缓存发音。" && requests == 5 && keyReads == 5 && statuses.Count == 1,
                        "offline mode reports the missing cached voice before reading a key or contacting the service");
                }
                try { await speech.OnlineAsync("word", new(SpeechCatalog.Local.Provider, SpeechCatalog.Local.Id, "auto"), true, Key, null, CancellationToken.None); Check(false, "local online"); }
                catch (InvalidOperationException e) { Check(e.Message == "此模型不使用在线语音接口。" && requests == 5, "local voices never reach an online speech service"); }
                try { await speech.ElevenVoicesAsync(false, "synthetic", CancellationToken.None); Check(false, "offline voice list"); }
                catch (InvalidOperationException e) { Check(e.Message == "请先开启在线补充，再读取声线列表。" && requests == 5, "account voices are not read in offline mode"); }
                var local = new SpeechOptions(SpeechCatalog.Local.Provider, SpeechCatalog.Local.Id, "auto");
                var wave = await speech.PrepareAsync("word", local, false, (temp, token) => File.WriteAllBytesAsync(temp, [1, 2, 3], token), null, CancellationToken.None);
                Check(wave.EndsWith(".wav") && File.Exists(wave) && wave != path, "front-end local synthesis uses the same cache rules and works offline");
            }

            var before = Directory.GetFiles(folder).Length;
            using (var speech = new SpeechAudio(root, new SpeechClient(new Handler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not audio") })))))
            {
                try { await speech.OnlineAsync("broken", openai, true, () => "synthetic", null, CancellationToken.None); Check(false, "invalid audio"); }
                catch (InvalidDataException) { Check(Directory.GetFiles(folder).Length == before, "failed generation leaves neither a partial nor a cached file"); }
            }
            using (var cancel = new CancellationTokenSource())
            using (var speech = new SpeechAudio(root, new SpeechClient(new Handler(async (_, token) => { cancel.Cancel(); await Task.Delay(1000, token); return new(HttpStatusCode.OK); }))))
            {
                try { await speech.OnlineAsync("cancelled", openai, true, () => "synthetic", null, cancel.Token); Check(false, "cancelled generation"); }
                catch (OperationCanceledException) { Check(Directory.GetFiles(folder).Length == before, "cancelled generation leaves no file behind"); }
            }
            using (var speech = new SpeechAudio(root, new SpeechClient(new Handler((request, _) =>
                throw new HttpRequestException("No such host is known. (" + request.RequestUri!.Host + ")")))))
            {
                try { await speech.OnlineAsync("offline", openai, true, () => "synthetic", null, CancellationToken.None); Check(false, "unreachable speech"); }
                catch (InvalidOperationException e) { Check(e.Message == "无法连接语音服务，请检查网络后重试。", "unreachable services get the same wording on every platform"); }
                try { await speech.ElevenVoicesAsync(true, "synthetic", CancellationToken.None); Check(false, "unreachable voices"); }
                catch (InvalidOperationException e) { Check(e.Message == "无法连接 ElevenLabs，请检查网络后重试。", "unreachable ElevenLabs voice lists keep their existing wording"); }
            }

            var other = SpeechCatalog.IsLocal("windows") ? "macos" : "windows";
            File.WriteAllText(Path.Combine(root, "settings.json"), $"{{\"speechProvider\":\"{other}\",\"speechModel\":\"x\",\"speechVoice\":\"other-system-voice\",\"speechVoiceName\":\"Other\"}}");
            var adopted = AppSettings.Load(root);
            File.WriteAllText(Path.Combine(root, "settings.json"), "{\"speechProvider\":\"elevenlabs\",\"speechModel\":\"eleven_v3\",\"speechVoice\":\"voice_a\",\"speechVoiceName\":\"Alpha\"}");
            var online = AppSettings.Load(root);
            Check(adopted is { SpeechVoice: "auto", SpeechVoiceName: "" } && adopted.SpeechProvider == SpeechCatalog.Local.Provider && adopted.SpeechModel == SpeechCatalog.Local.Id &&
                online is { SpeechProvider: "elevenlabs", SpeechModel: "eleven_v3", SpeechVoice: "voice_a", SpeechVoiceName: "Alpha" },
                "local speech saved on the other platform stays local with automatic voice choice; online choices are kept");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken); }
}
