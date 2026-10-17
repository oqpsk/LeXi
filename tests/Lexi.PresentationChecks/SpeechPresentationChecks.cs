using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using Lexi;
using Lexi.Core;

internal static partial class Program
{
    private static void CheckSpeechPresentation(MainWindow main, SettingsWindow settings)
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
        var tabs = Contents(settings).OfType<TabControl>().Single();
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => (string)t.Header == "发音");
        Measure((FrameworkElement)settings.Content, 538, 478);
        var selectors = Descendants((DependencyObject)settings.Content).OfType<ComboBox>().ToArray();
        Check(selectors.Length == 2 && selectors[0].Items.Count == SpeechCatalog.Models.Length, "speech settings expose a model selector and a voice selector");
        selectors[0].SelectedItem = selectors[0].Items.OfType<SpeechModel>().Single(m => m.Id == "gpt-4o-mini-tts");
        Check(selectors[1].Items.Count == 13 && selectors[1].SelectedItem != null, "changing the speech model refreshes compatible voices");
        selectors[0].SelectedItem = selectors[0].Items.OfType<SpeechModel>().Single(m => m.Provider == "xai");
        Check(selectors[1].Items.OfType<SpeechVoice>().Any(v => v.Id == "eve") && Contents(settings).OfType<PasswordBox>().Count() == 3,
            "Grok voices and masked API key fields are available in settings");
        selectors[0].SelectedItem = selectors[0].Items.OfType<SpeechModel>().Single(m => m.Id == "eleven_flash_v2_5");
        Check(selectors[1].Items.OfType<SpeechVoice>().Any(v => v.Name == "George") &&
            Contents(settings).OfType<Button>().Any(b => AutomationProperties.GetName(b) == "刷新 ElevenLabs 可用声线列表"),
            "ElevenLabs offers an initial voice and explicitly triggered account voice loading");
        Check(SpeechVoiceChoices.Eleven(null, null).Length == 1 && SpeechVoiceChoices.Eleven([], null).Length == 0,
            "an empty fetched voice list stays distinct from the initial unfetched fallback");
        CheckSpeechDiscovery(Check);
        var savedVoice = new SpeechVoice("synthetic_private_voice", "Synthetic private voice");
        Check(SpeechVoiceChoices.Eleven([], savedVoice).Any(v => v == savedVoice) &&
            SpeechVoiceChoices.Eleven([savedVoice], savedVoice).Length == 1,
            "saved custom voices survive without an account lookup and do not duplicate loaded choices");
        Check(SpeechVoiceChoices.Pick("elevenlabs", savedVoice, " other_voice ")?.Id == "other_voice" &&
            SpeechVoiceChoices.Pick("openai", savedVoice, "ignored") == savedVoice,
            "a manual Voice ID applies only to ElevenLabs and remains independent of the voice name");
        var speech = (Button)main.FindName("SpeechButton");
        Check(!Theme.CanDragFrom(speech) && AutomationProperties.GetName(speech).Contains("合成"), "pronunciation button is accessible and never drags the window");
        foreach (var name in new[] { "SpeakerIcon", "StopIcon" })
        { var icon = Theme.Icon(name); Check(!icon.Data.Bounds.IsEmpty, name + " uses vector geometry"); }
        var model = new LookupModel("codex", "fixture");
        var answer = new ModelAnswer(model, ModelPhase.Complete, new("word", true,
            [new("noun", "definition", "释义", "", "Example.", "例句。", [])], "", "unverified", "/wɜːd/"));
        main.RenderAnswers(new([answer], SenseMerging.Group([answer])), null);
        var phonetic = (Run)main.FindName("Phonetic");
        var label = (Run)main.FindName("PhoneticLabel");
        Check(phonetic.Text.Contains("/wɜːd/") && label.Text.Contains("AI") && phonetic.ToolTip.ToString()!.Contains("未经"),
            "model pronunciation is displayed with an explicit unverified AI label");
        var work = Path.Combine(Environment.CurrentDirectory, ".cache", "speech-presentation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var keys = new SpeechKeys(work); var synthetic = "test-" + Guid.NewGuid().ToString("N");
        keys.Save("openai", synthetic);
        var bytes = File.ReadAllBytes(Path.Combine(work, "speech-keys", "openai.bin"));
        Check(keys.Read("openai") == synthetic && !Encoding.UTF8.GetString(bytes).Contains(synthetic), "speech keys round-trip through current-user Windows encryption, never plaintext settings");
        keys.Save("openai", ""); Check(!keys.HasSaved("openai"), "clearing a saved speech key removes only its own encrypted file");
        keys.Save("elevenlabs", synthetic);
        Check(keys.Read("elevenlabs") == synthetic && !Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(work,"speech-keys","elevenlabs.bin"))).Contains(synthetic),
            "ElevenLabs keys use isolated current-user encryption");
        keys.Save("elevenlabs", "");
        Check(!keys.HasSaved("elevenlabs") && !keys.HasSaved("openai"), "removing an ElevenLabs key does not recreate or change another provider key");
        var voices = LocalSpeech.Voices();
        if (voices.Length > 1)
        {
            var output = Path.Combine(work, "synthetic.wav");
            LocalSpeech.GenerateAsync("Hello, Lexi.", voices[1].Id, output, CancellationToken.None).GetAwaiter().GetResult();
            var wav = File.ReadAllBytes(output);
            Check(wav.Length > 44 && Encoding.ASCII.GetString(wav, 0, 4) == "RIFF" && Encoding.ASCII.GetString(wav, 8, 4) == "WAVE",
                "Windows voice generates a valid WAV file without playing audio or opening a window");
        }
        else Console.WriteLine("SKIP: no installed SAPI voice; local synthesis is unavailable on this host.");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel(); var output = Path.Combine(work, "canceled.wav");
            try { LocalSpeech.GenerateAsync("Do not synthesize.", "auto", output, cancel.Token).GetAwaiter().GetResult(); Check(false, "local speech cancellation"); }
            catch (OperationCanceledException) { Check(!File.Exists(output), "canceled local speech never opens an output file or plays audio"); }
        }
        Console.WriteLine($"{count} speech presentation and native checks passed");
    }
    private static void CheckSpeechDiscovery(Action<bool, string> check)
    {
        int requests = 0; bool reject = false;
        using var client = new SpeechClient(new SpeechMetadataHandler((request, _) =>
        {
            requests++;
            check(request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/v2/voices",
                "settings discovery never synthesizes speech");
            return Task.FromResult(new HttpResponseMessage(reject ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
            {
                Content = new StringContent(reject
                    ? "{\"detail\":{\"status\":\"api_key_id_used_as_api_key\",\"message\":\"private-message\"}}"
                    : "{\"voices\":[{\"voice_id\":\"voice_a\",\"name\":\"Alpha\"},{\"voice_id\":\"voice_b\",\"name\":\"Beta\"}],\"has_more\":false}")
            });
        }));
        var fixture = new SettingsWindow(new AppSettings { Online = true, SpeechProvider = "elevenlabs",
            SpeechModel = "eleven_flash_v2_5", SpeechVoice = "voice_a", SpeechVoiceName = "Alpha" }, [], client);
        var picker = Contents(fixture).OfType<SpeechVoicePicker>().Single();
        var key = Contents(fixture).OfType<PasswordBox>().Last();
        var connectionStatus = Contents(fixture).OfType<TextBlock>().Single(b => AutomationProperties.GetName(b) == "声线连接状态");
        check(requests == 0, "constructing settings does not send account requests");
        check(picker.Style == (Style)Application.Current.FindResource(typeof(ComboBox)),
            "the voice picker retains the app's dark dropdown style");
        key.Password = "synthetic-key";
        picker.DiscoverAsync().GetAwaiter().GetResult();
        check(requests == 1 && picker.Items.Count == 2 && ((SpeechVoice)picker.SelectedItem).Id == "voice_a",
            "first voice-list expansion fetches account choices and preserves the selected voice");
        picker.DiscoverAsync().GetAwaiter().GetResult();
        check(requests == 1, "reopening the fetched list reuses its choices without a redundant request");
        picker.DiscoverAsync(true).GetAwaiter().GetResult();
        check(requests == 2, "explicit refresh fetches voice choices again");
        key.Password = "synthetic-replacement";
        reject = true;
        picker.DiscoverAsync().GetAwaiter().GetResult();
        check(requests == 3 && connectionStatus.Text.Contains("API Key ID") && !connectionStatus.Text.Contains("private"),
            "key edits invalidate voice discovery and show safe connection errors beside the selector");
        reject = false;
        picker.DiscoverAsync().GetAwaiter().GetResult();
        check(requests == 4 && picker.Items.Count == 2 && connectionStatus.Text.Contains("2 条"),
            "failed discovery can be retried by reopening the list");
        fixture.Close();
    }
    private sealed class SpeechMetadataHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

}
