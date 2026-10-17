using System.Windows.Media;
using System.Windows.Threading;
using Lexi.Core;

namespace Lexi;

internal sealed class SpeechPlayback(string root) : IDisposable
{
    // Cache naming, the offline rule and complete-file writes are shared with macOS; SAPI and keys stay here.
    private readonly SpeechAudio files = new(root);
    private CancellationTokenSource? active;
    private MediaPlayer? player;
    public void Stop() { active?.Cancel(); player?.Close(); player = null; }
    public async Task PlayAsync(string text, SpeechOptions options, bool online, Action<string> status, string? suppliedKey = null)
    {
        Stop(); active?.Dispose(); var request = active = new CancellationTokenSource(); var token = request.Token;
        var path = options.Provider == "windows"
            ? await files.PrepareAsync(text, options, online, (temp, stop) => LocalSpeech.GenerateAsync(text, options.Voice, temp, stop), status, token)
            : await files.OnlineAsync(text, options, online, () => suppliedKey ?? new SpeechKeys(root).Read(options.Provider), status, token);
        token.ThrowIfCancellationRequested();
        var audio = new MediaPlayer(); player = audio;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        audio.MediaEnded += (_, _) => done.TrySetResult();
        audio.MediaFailed += (_, _) => done.TrySetException(new InvalidOperationException("音频无法播放，请检查系统音频输出。"));
        using var cancel = token.Register(() => audio.Dispatcher.BeginInvoke(() => { audio.Close(); done.TrySetCanceled(token); }));
        try
        {
            status("正在播放合成发音…"); audio.Open(new Uri(path)); audio.Play();
            await done.Task.WaitAsync(TimeSpan.FromMinutes(5), token);
        }
        finally { audio.Close(); if (ReferenceEquals(player, audio)) player = null; }
    }
    public void Dispose() { Stop(); active?.Dispose(); files.Dispose(); }
}
