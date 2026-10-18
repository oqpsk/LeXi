using Lexi.Core;

namespace Lexi;

internal sealed class SpeechPlayback(string root) : IDisposable
{
    // Cache naming, the offline rule and complete-file writes are shared with macOS; SAPI and keys stay here.
    private readonly SpeechAudio files = new(root);
    private CancellationTokenSource? active;
    private SpeechOutput? output;
    public void Stop() { active?.Cancel(); output?.Dispose(); output = null; }
    public async Task PlayAsync(string text, SpeechOptions options, bool online, Action<string> status, string? suppliedKey = null, Action? started = null)
    {
        Stop(); active?.Dispose(); var request = active = new CancellationTokenSource(); var token = request.Token;
        var path = options.Provider == "windows"
            ? await files.PrepareAsync(text, options, online, (temp, stop) => LocalSpeech.GenerateAsync(text, options.Voice, temp, stop), status, token)
            : await files.OnlineAsync(text, options, online, () => suppliedKey ?? new SpeechKeys(root).Read(options.Provider), status, token);
        token.ThrowIfCancellationRequested();
        status("正在准备音频输出…");
        using var padded = await SpeechPcmFile.PrepareAsync(path, token);
        token.ThrowIfCancellationRequested();
        using var playback = new SpeechOutput(); output = playback;
        try
        {
            await playback.PlayAsync(padded.Path, () => { status("正在播放合成发音…"); started?.Invoke(); }, token);
        }
        finally { if (ReferenceEquals(output, playback)) output = null; }
    }
    public void Dispose() { Stop(); active?.Dispose(); files.Dispose(); }
}
