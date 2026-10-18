using System.Windows.Media;

namespace Lexi;

// Padding and speech share this one output stream. Priming a separate MediaPlayer does not
// protect the next stream's first samples when the output/codec starts cold.
internal sealed class SpeechOutput(ISpeechTrack? voice = null) : IDisposable
{
    internal static readonly TimeSpan LeadIn = TimeSpan.FromMilliseconds(600);
    private readonly ISpeechTrack voice = voice ?? new MediaSpeechTrack();
    public async Task PlayAsync(string paddedPath, Action started, CancellationToken token)
    {
        await voice.OpenAsync(paddedPath, token);
        token.ThrowIfCancellationRequested();
        voice.PlayFromStart();
        await voice.WaitForProgressAsync(LeadIn, token);
        token.ThrowIfCancellationRequested();
        started();
        await voice.WaitForEndAsync(token);
    }
    public void Dispose() => voice.Dispose();
}

internal interface ISpeechTrack : IDisposable
{
    Task OpenAsync(string path, CancellationToken token);
    void PlayFromStart();
    Task WaitForProgressAsync(TimeSpan position, CancellationToken token);
    Task WaitForEndAsync(CancellationToken token);
}

internal sealed class MediaSpeechTrack : ISpeechTrack
{
    private readonly MediaPlayer audio = new();
    private readonly TaskCompletionSource opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<Exception> failed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool disposed;
    public MediaSpeechTrack()
    {
        audio.MediaOpened += (_, _) => opened.TrySetResult();
        audio.MediaEnded += (_, _) => ended.TrySetResult();
        audio.MediaFailed += (_, _) => failed.TrySetResult(new InvalidOperationException("音频无法播放，请检查系统音频输出。"));
    }
    public async Task OpenAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        audio.Open(new Uri(path));
        await WaitAsync(opened.Task, TimeSpan.FromSeconds(10), token);
    }
    public void PlayFromStart()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!opened.Task.IsCompletedSuccessfully) throw new InvalidOperationException("音频尚未准备好。");
        audio.Position = TimeSpan.Zero;
        audio.Play();
    }
    public async Task WaitForProgressAsync(TimeSpan position, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            // Use the running media clock, rather than elapsed time since Open/Play was called.
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (failed.Task.IsCompletedSuccessfully) throw await failed.Task;
                if (ended.Task.IsCompleted) throw new InvalidOperationException("音频输出准备未完成，请重试。");
                if (audio.Position >= position) return;
                await Task.Delay(15, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("音频输出准备超时，请检查扬声器后重试。"); }
    }
    public Task WaitForEndAsync(CancellationToken token) => WaitAsync(ended.Task, TimeSpan.FromMinutes(5), token);
    private async Task WaitAsync(Task target, TimeSpan timeout, CancellationToken token)
    {
        try { await Task.WhenAny(target, failed.Task).WaitAsync(timeout, token); }
        catch (TimeoutException) { throw new TimeoutException("音频播放超时，请检查系统音频输出后重试。"); }
        token.ThrowIfCancellationRequested();
        if (failed.Task.IsCompletedSuccessfully) throw await failed.Task;
        await target;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        audio.Close();
    }
}
