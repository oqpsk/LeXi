using System.IO;
using System.Text;
using Lexi;

internal static partial class Program
{
    private static async Task CheckSpeechPlayback()
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
        static async Task Play(SpeechTrackFixture voice, SpeechTrackFixture primer, CancellationToken token, Action? started = null)
        {
            using var output = new SpeechOutput(voice, primer);
            await output.PlayAsync("synthetic-speech.mp3", "synthetic-silence.wav", started ?? (() => { }), token);
        }
        static async Task Canceled(Task task)
        {
            try { await task.WaitAsync(TimeSpan.FromSeconds(5)); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { }
        }
        var voice = new SpeechTrackFixture(); var primer = new SpeechTrackFixture(); int starts = 0;
        var playback = Play(voice, primer, CancellationToken.None, () => starts++);
        Check(voice.Path == "synthetic-speech.mp3" && primer.Path == null && !voice.Played,
            "speech remains at the beginning while its local file is opening");
        voice.Opened.SetResult(); await primer.OpenEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(!primer.Played && !voice.Played && starts == 0, "neither stream starts before the silent primer is ready");
        primer.Opened.SetResult(); await primer.ProgressEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(primer.Played && !voice.Played && primer.PositionTarget >= TimeSpan.FromMilliseconds(350) && starts == 0,
            "only silence runs while the output clock warms up, without consuming any speech");
        primer.Progress.SetResult(); await voice.EndEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(voice.Played && !primer.Disposed && starts == 1 && !playback.IsCompleted,
            "speech starts once the output is warm, overlapping the silent stream until completion");
        voice.Ended.SetResult(); await playback.WaitAsync(TimeSpan.FromSeconds(5));
        Check(voice.Disposed && primer.Disposed, "normal completion closes both output streams");

        foreach (var phase in new[] { "opening", "priming", "speaking" })
        {
            using var cancel = new CancellationTokenSource();
            voice = new(); primer = new();
            playback = Play(voice, primer, cancel.Token);
            if (phase != "opening")
            {
                voice.Opened.SetResult(); primer.Opened.SetResult();
                await primer.ProgressEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            if (phase == "speaking")
            {
                primer.Progress.SetResult(); await voice.EndEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            cancel.Cancel(); await Canceled(playback);
            voice.Opened.TrySetResult(); primer.Opened.TrySetResult(); primer.Progress.TrySetResult();
            Check(voice.Disposed && primer.Disposed && (phase == "speaking" || !voice.Played),
                $"stop during {phase} closes both tracks and rejects late readiness without starting speech");
        }
        foreach (var phase in new[] { "opening", "priming", "speaking" })
        {
            voice = new(); primer = new(); playback = Play(voice, primer, CancellationToken.None);
            var failure = new InvalidOperationException("Synthetic output failure");
            if (phase == "opening") voice.Opened.SetException(failure);
            else
            {
                voice.Opened.SetResult(); primer.Opened.SetResult();
                await primer.ProgressEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (phase == "priming") primer.Progress.SetException(failure);
                else { primer.Progress.SetResult(); await voice.EndEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); voice.Ended.SetException(failure); }
            }
            try { await playback.WaitAsync(TimeSpan.FromSeconds(5)); Check(false, "output failure"); }
            catch (InvalidOperationException ex) when (ReferenceEquals(ex, failure))
            { Check(voice.Disposed && primer.Disposed, $"failure during {phase} surfaces and releases every output stream"); }
        }
        using (var cancel = new CancellationTokenSource())
        {
            var oldVoice = new SpeechTrackFixture(); var oldPrimer = new SpeechTrackFixture();
            var old = Play(oldVoice, oldPrimer, cancel.Token);
            cancel.Cancel();
            voice = new(); primer = new(); playback = Play(voice, primer, CancellationToken.None);
            voice.Opened.SetResult(); primer.Opened.SetResult(); primer.Progress.SetResult();
            await voice.EndEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); await Canceled(old);
            Check(voice.Played && !voice.Disposed && !primer.Disposed && oldVoice.Disposed && oldPrimer.Disposed,
                "a replaced clip cannot close or restart the new clip's output");
            voice.Ended.SetResult(); await playback.WaitAsync(TimeSpan.FromSeconds(5));
        }
        var root = System.IO.Path.Combine(Environment.CurrentDirectory, ".cache", "speech-output", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(System.IO.Path.Combine(root, "speech-cache"));
        var original = System.IO.Path.Combine(root, "speech-cache", "synthetic.mp3");
        var sourceBytes = Encoding.ASCII.GetBytes("ID3 original speech cache is never rewritten"); File.WriteAllBytes(original, sourceBytes);
        var silence = SpeechOutput.PrepareSilence(root); var bytes = File.ReadAllBytes(silence);
        Check(Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && BitConverter.ToInt32(bytes, 4) == bytes.Length - 8 &&
            Encoding.ASCII.GetString(bytes, 8, 8) == "WAVEfmt " && BitConverter.ToInt16(bytes, 20) == 1 &&
            BitConverter.ToInt16(bytes, 22) == 1 && BitConverter.ToInt32(bytes, 24) == 44100 &&
            BitConverter.ToInt32(bytes, 28) == 88200 && BitConverter.ToInt16(bytes, 32) == 2 && BitConverter.ToInt16(bytes, 34) == 16 &&
            Encoding.ASCII.GetString(bytes, 36, 4) == "data" && BitConverter.ToInt32(bytes, 40) == 88200 && bytes.Skip(44).All(b => b == 0),
            "the output primer is one second of valid mono 16-bit PCM silence, long enough to overlap speech startup");
        var timestamp = DateTime.UtcNow.AddDays(-1); File.SetLastWriteTimeUtc(silence, timestamp);
        Check(SpeechOutput.PrepareSilence(root) == silence && File.GetLastWriteTimeUtc(silence) == timestamp &&
            File.ReadAllBytes(original).SequenceEqual(sourceBytes) && Directory.GetFiles(System.IO.Path.GetDirectoryName(silence)!, "*.tmp").Length == 0,
            "repeat playback reuses complete local silence and leaves provider audio byte-for-byte unchanged");
    }
    private sealed class SpeechTrackFixture : ISpeechTrack
    {
        public readonly TaskCompletionSource Opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Progress = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource OpenEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ProgressEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource EndEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Path; public bool Played, Disposed; public TimeSpan PositionTarget;
        public Task OpenAsync(string path, CancellationToken token) { Path = path; OpenEntered.TrySetResult(); return Opened.Task.WaitAsync(token); }
        public void PlayFromStart()
        { if (Disposed || !Opened.Task.IsCompletedSuccessfully) throw new Exception("Playback before readiness or after disposal"); Played = true; }
        public Task WaitForProgressAsync(TimeSpan position, CancellationToken token)
        { if (!Played) throw new Exception("No running output clock"); PositionTarget = position; ProgressEntered.TrySetResult(); return Progress.Task.WaitAsync(token); }
        public Task WaitForEndAsync(CancellationToken token) { EndEntered.TrySetResult(); return Ended.Task.WaitAsync(token); }
        public void Dispose() => Disposed = true;
    }
}
