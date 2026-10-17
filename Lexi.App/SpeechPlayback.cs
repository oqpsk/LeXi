using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using Lexi.Core;

namespace Lexi;

internal sealed class SpeechPlayback(string root) : IDisposable
{
    private readonly SpeechClient client = new();
    private CancellationTokenSource? active;
    private MediaPlayer? player;
    public void Stop() { active?.Cancel(); player?.Close(); player = null; }
    public async Task PlayAsync(string text, SpeechOptions options, bool online, Action<string> status, string? suppliedKey = null)
    {
        Stop(); active?.Dispose(); var request = active = new CancellationTokenSource(); var token = request.Token;
        SpeechCatalog.Validate(text, options);
        var folder = Path.Combine(root, "speech-cache"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, options.CacheKey(text) + (options.Provider == "windows" ? ".wav" : ".mp3"));
        if (!File.Exists(path))
        {
            if (options.Provider != "windows" && !online) throw new InvalidOperationException("离线模式下没有此声线的缓存发音。");
            status("正在生成发音…");
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                if (options.Provider == "windows") await LocalSpeech.GenerateAsync(text, options.Voice, temp, token);
                else
                {
                    var bytes = await client.GenerateAsync(text, options, suppliedKey ?? new SpeechKeys(root).Read(options.Provider), token);
                    await File.WriteAllBytesAsync(temp, bytes, token);
                }
                token.ThrowIfCancellationRequested(); File.Move(temp, path, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
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
    public void Dispose() { Stop(); active?.Dispose(); client.Dispose(); }
}
