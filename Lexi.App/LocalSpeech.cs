using System.Globalization;
using System.Runtime.InteropServices;
using Lexi.Core;

namespace Lexi;

internal static class LocalSpeech
{
    private static dynamic Create(string name) => Activator.CreateInstance(Type.GetTypeFromProgID(name)
        ?? throw new InvalidOperationException("此系统没有可用的本地发音组件。"))!;
    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    public static SpeechVoice[] Voices()
    {
        object? engine = null, collection = null;
        try
        {
            dynamic speaker = engine = Create("SAPI.SpVoice"); dynamic voices = collection = speaker.GetVoices();
            var result = new List<SpeechVoice> { new("auto", "按文字语言自动选择") };
            for (int i = 0; i < (int)voices.Count; i++)
            {
                object? item = null;
                try { dynamic voice = item = voices.Item(i); result.Add(new((string)voice.Id, (string)voice.GetDescription() + " · " + Language(voice))); }
                finally { Release(item); }
            }
            return result.ToArray();
        }
        catch (Exception e) when (e is COMException or InvalidOperationException) { return []; }
        finally { Release(collection); Release(engine); }
    }
    private static string Language(dynamic voice)
    {
        var value = ((string)voice.GetAttribute("Language")).Split(';')[0];
        return int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var lcid) ? CultureInfo.GetCultureInfo(lcid).Name : "";
    }
    public static Task GenerateAsync(string text, string voiceId, string path, CancellationToken token)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            object? engine = null, collection = null, chosen = null, output = null;
            Exception? failure = null;
            try
            {
                token.ThrowIfCancellationRequested();
                dynamic speaker = engine = Create("SAPI.SpVoice"); dynamic voices = collection = speaker.GetVoices();
                var language = Queries.HasChinese(text) ? "zh" : "en";
                for (int i = 0; i < (int)voices.Count; i++)
                {
                    dynamic voice = voices.Item(i);
                    if (voiceId == "auto" ? Language(voice).StartsWith(language, StringComparison.OrdinalIgnoreCase) : (string)voice.Id == voiceId)
                    { chosen = voice; break; }
                    Release(voice);
                }
                if (chosen == null) throw new InvalidOperationException("没有匹配的 Windows 声线，请在设置 → 发音中另选声线或在线服务。");
                speaker.Voice = chosen;
                dynamic stream = output = Create("SAPI.SpFileStream"); stream.Open(path, 3, false); speaker.AudioOutputStream = stream;
                // SVSFlagsAsync | SVSFIsNotXML: never interpret queried text as SSML.
                speaker.Speak(text, 1 | 16);
                while (!speaker.WaitUntilDone(50))
                {
                    if (token.IsCancellationRequested) { speaker.Speak("", 2 | 16); token.ThrowIfCancellationRequested(); }
                }
                token.ThrowIfCancellationRequested(); stream.Close();
            }
            catch (Exception e) { failure = e; }
            finally
            {
                if (output != null) { try { ((dynamic)output).Close(); } catch (COMException) { } }
                Release(output); Release(chosen); Release(collection); Release(engine);
            }
            // Finish only after releasing the file so cancellation can safely remove it.
            if (failure is OperationCanceledException) completion.TrySetCanceled(token);
            else if (failure != null) completion.TrySetException(new InvalidOperationException("本地发音未能生成。" + failure.Message, failure));
            else completion.TrySetResult();
        }) { IsBackground = true, Name = "Lexi speech synthesis" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
