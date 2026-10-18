using System.IO;
using System.Text;
using Lexi;

internal static partial class Program
{
    private static async Task CheckSpeechPlayback()
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
        static async Task Play(SpeechTrackFixture track, CancellationToken token, Action? started = null)
        { using var output = new SpeechOutput(track); await output.PlayAsync("synthetic-padded.wav",started ?? (() => { }),token); }
        static async Task Canceled(Task task)
        { try { await task.WaitAsync(TimeSpan.FromSeconds(5)); throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { } }
        var track = new SpeechTrackFixture(); int starts = 0;
        var playback = Play(track,CancellationToken.None,() => starts++);
        Check(track.Path == "synthetic-padded.wav" && !track.Played, "one padded speech stream waits for its decoder before playback");
        track.Opened.SetResult(); await track.ProgressEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(track.Played && track.PositionTarget == SpeechOutput.LeadIn && starts == 0,
            "the speech file's own clock consumes its silent lead-in before reporting audible playback");
        track.Progress.SetResult(); await track.EndEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(starts == 1 && !track.Disposed && !playback.IsCompleted, "a single stream continues into speech without opening or restarting another output");
        track.Ended.SetResult(); await playback.WaitAsync(TimeSpan.FromSeconds(5));
        Check(track.Disposed, "completion releases the only output stream");
        foreach (var phase in new[] { "opening", "lead-in", "speaking" })
        {
            using var stop = new CancellationTokenSource(); track = new();
            playback = Play(track,stop.Token);
            if (phase != "opening") { track.Opened.SetResult(); await track.ProgressEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            if (phase == "speaking") { track.Progress.SetResult(); await track.EndEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            stop.Cancel(); await Canceled(playback);
            track.Opened.TrySetResult(); track.Progress.TrySetResult();
            Check(track.Disposed, $"cancellation during {phase} closes playback and ignores late readiness");
        }
        foreach (var phase in new[] { "opening", "lead-in", "speaking" })
        {
            track = new(); playback = Play(track,CancellationToken.None);
            var failure = new InvalidOperationException("Synthetic failure");
            if (phase == "opening") track.Opened.SetException(failure);
            else
            {
                track.Opened.SetResult(); await track.ProgressEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (phase == "lead-in") track.Progress.SetException(failure);
                else { track.Progress.SetResult(); await track.EndEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); track.Ended.SetException(failure); }
            }
            try { await playback.WaitAsync(TimeSpan.FromSeconds(5)); Check(false,"failure must surface"); }
            catch (InvalidOperationException ex) when (ReferenceEquals(ex,failure)) { Check(track.Disposed,$"failure during {phase} releases the output"); }
        }
        using (var stop = new CancellationTokenSource())
        {
            var oldTrack = new SpeechTrackFixture(); var old = Play(oldTrack,stop.Token);
            stop.Cancel(); track = new(); playback = Play(track,CancellationToken.None);
            track.Opened.SetResult(); track.Progress.SetResult(); await track.EndEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); await Canceled(old);
            Check(track.Played && !track.Disposed && oldTrack.Disposed,"stale playback cannot stop or restart its replacement");
            track.Ended.SetResult(); await playback.WaitAsync(TimeSpan.FromSeconds(5));
        }

        // Exercise the real Windows decoder with generated PCM, without opening an audio device.
        var work = System.IO.Path.Combine(Environment.CurrentDirectory,".cache","speech-output",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        foreach (var channels in new[] { 1, 2 })
        {
            var source = System.IO.Path.Combine(work,$"synthetic-{channels}.wav");
            const int rate = 24000;
            var samples = Enumerable.Range(0,4800*channels).SelectMany(i => BitConverter.GetBytes((short)(1000+i%700))).ToArray();
            using (var writer = new BinaryWriter(File.Create(source),Encoding.ASCII))
            {
                writer.Write("RIFF"u8); writer.Write(36+samples.Length); writer.Write("WAVEfmt "u8); writer.Write(16);
                writer.Write((short)1); writer.Write((short)channels); writer.Write(rate); writer.Write(rate*channels*2);
                writer.Write((short)(channels*2)); writer.Write((short)16); writer.Write("data"u8); writer.Write(samples.Length); writer.Write(samples);
            }
            var original = File.ReadAllBytes(source);
            string prepared;
            using (var padded = await SpeechPcmFile.PrepareAsync(source,CancellationToken.None))
            {
                prepared = padded.Path; var bytes = File.ReadAllBytes(prepared);
                int silence = (int)(rate*SpeechOutput.LeadIn.TotalSeconds)*channels*2;
                Check(Encoding.ASCII.GetString(bytes,0,4)=="RIFF" && BitConverter.ToInt32(bytes,4)==bytes.Length-8 &&
                    BitConverter.ToInt16(bytes,22)==channels && BitConverter.ToInt32(bytes,24)==rate && BitConverter.ToInt32(bytes,40)==silence+samples.Length,
                    $"native decoding retains the {channels}-channel PCM format and writes complete padded WAV lengths");
                Check(bytes.Skip(44).Take(silence).All(b=>b==0) && bytes.Skip(44+silence).SequenceEqual(samples),
                    $"every original {channels}-channel sample, including the first, follows silence unchanged in the same stream");
            }
            Check(!File.Exists(prepared) && File.ReadAllBytes(source).SequenceEqual(original),"temporary playback audio is removed and the original cache stays byte-for-byte intact");
            using var stopped = new CancellationTokenSource(); stopped.Cancel();
            await Canceled(SpeechPcmFile.PrepareAsync(source,stopped.Token));
        }
        if (Environment.GetEnvironmentVariable("LEXI_SPEECH_CODEC_FIXTURE") is { Length: > 0 } fixture)
        {
            var original = File.ReadAllBytes(fixture);
            using var padded = await SpeechPcmFile.PrepareAsync(fixture,CancellationToken.None);
            var bytes = File.ReadAllBytes(padded.Path);
            var silence = (int)(BitConverter.ToInt32(bytes,24)*SpeechOutput.LeadIn.TotalSeconds)*BitConverter.ToInt16(bytes,32);
            Check(bytes.Skip(44).Take(silence).All(b=>b==0) && bytes.Skip(44+silence).Any(b=>b!=0) && File.ReadAllBytes(fixture).SequenceEqual(original),
                "the real MP3 decoder produces silent lead-in followed by nonempty audio without changing compressed bytes");
        }
        var broken = System.IO.Path.Combine(work,"invalid.mp3"); File.WriteAllText(broken,"not audio");
        try { using var padded = await SpeechPcmFile.PrepareAsync(broken,CancellationToken.None); Check(false,"invalid audio rejected"); }
        catch (InvalidOperationException) { Check(true,"invalid encoded audio reports a friendly decoding error"); }
    }
    private sealed class SpeechTrackFixture : ISpeechTrack
    {
        public readonly TaskCompletionSource Opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Progress = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ProgressEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource EndEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Path; public bool Played, Disposed; public TimeSpan PositionTarget;
        public Task OpenAsync(string path,CancellationToken token) { Path=path; return Opened.Task.WaitAsync(token); }
        public void PlayFromStart() { if (Disposed || !Opened.Task.IsCompletedSuccessfully) throw new Exception("Not ready"); Played=true; }
        public Task WaitForProgressAsync(TimeSpan position,CancellationToken token) { PositionTarget=position; ProgressEntered.TrySetResult(); return Progress.Task.WaitAsync(token); }
        public Task WaitForEndAsync(CancellationToken token) { EndEntered.TrySetResult(); return Ended.Task.WaitAsync(token); }
        public void Dispose() => Disposed=true;
    }
}
