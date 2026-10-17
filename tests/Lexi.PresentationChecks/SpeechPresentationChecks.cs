using System.IO;
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
        Check(selectors.Length == 2 && selectors[0].Items.Count == 5, "speech settings expose a model selector and a voice selector");
        selectors[0].SelectedItem = selectors[0].Items.OfType<SpeechModel>().Single(m => m.Id == "gpt-4o-mini-tts");
        Check(selectors[1].Items.Count == 13 && selectors[1].SelectedItem != null, "changing the speech model refreshes compatible voices");
        selectors[0].SelectedItem = selectors[0].Items.OfType<SpeechModel>().Single(m => m.Provider == "xai");
        Check(selectors[1].Items.OfType<SpeechVoice>().Any(v => v.Id == "eve") && Contents(settings).OfType<PasswordBox>().Count() == 2,
            "Grok voices and masked API key fields are available in settings");
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
}
