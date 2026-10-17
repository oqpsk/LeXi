using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Lexi;
using Lexi.Core;

internal static partial class Program
{
    private static void CheckPaletteAndSavedView(App app, MainWindow main, SettingsWindow settings)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
        var pink = ((SolidColorBrush)app.FindResource("Accent")).Color;
        Check(new[] {"Selection", "AccentSoft", "AccentSurface", "AccentBorder"}.All(k => ((SolidColorBrush)app.FindResource(k)).Color == pink),
            "selection, translucent cards and borders all derive from the same pale pink");
        var save = Contents(settings).OfType<Button>().Single(b => b.Content as string == "保存");
        Check(save.Background is SolidColorBrush fill && fill.Color == pink && save.Foreground is SolidColorBrush ink && Contrast(ink.Color, pink) >= 7 &&
            ((Border)main.FindName("QuickReadCard")).Background == app.FindResource("AccentSoft"),
            "primary save button uses brand pink with dark text and overview reuses the shared tint");
        using (var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Lexi;component/Assets/Lexi.ico")).Stream)
        using (var logo = new System.Drawing.Icon(stream, 256, 256))
        using (var bitmap = logo.ToBitmap())
            Check(Enumerable.Range(0, bitmap.Width).Any(x => Enumerable.Range(0, bitmap.Height).Any(y =>
                bitmap.GetPixel(x,y) is var pixel && pixel.R == pink.R && pixel.G == pink.G && pixel.B == pink.B)),
                "packaged application and tray logo use the same exact pink as UI accents");
        var actions = (Panel)main.FindName("SearchActions");
        var upper = new[] {(Button)main.FindName("SettingsButton"), (Button)main.FindName("CloseButton")};
        var lower = actions.Children.OfType<Button>().ToArray();
        foreach (var width in new[] {558,718,1078})
        {
            Measure((FrameworkElement)main.Content, width, 618);
            Check(lower.Length == 2 && Enumerable.Range(0,2).All(i => Math.Abs(upper[i].TranslatePoint(new Point(16,16),main).X -
                lower[i].TranslatePoint(new Point(16,16),main).X) < 0.1), $"upper-right controls share two exact column centers at {width} DIP");
        }
        main.ShowHistory(); main.ResetWordForms();
        var model = new LookupModel("codex","previous-model");
        var sense = new AiSense("noun","A stored definition.","保存的释义", "", "Stored example.", "保存的例句。", [], Gloss:"保存的释义", CoreMeaning:true);
        var entry = new AiEntry("fixture",true,[sense],"","unverified","/fɪk/",[],"保存的语源");
        var answer = new ModelAnswer(model,ModelPhase.Complete,entry);
        var saved = new SavedDictionary("fixture",[],[new("fixture",[new("s","复数",["fixtures"])],"Saved source","")],[],[],null,null,
            new([answer],SenseMerging.Group([answer])),new("/fɪk/"," AI","previous model","previous model",false),true,false,DateTimeOffset.UtcNow);
        var progress=(QueryProgressView)main.FindName("QueryProgress");
        var request=progress.Begin(QueryProgressState.Dictionary(true,true));
        main.RenderSavedDictionary(saved,request,new LookupTiming());
        Check(((Run)main.FindName("Phonetic")).Text=="/fɪk/" && ((TextBox)main.FindName("QuickReadText")).Text=="保存的释义" &&
            Contents((Panel)main.FindName("FormsPanel")).OfType<Run>().Any(r=>r.Text=="fixtures") &&
            ((FrameworkElement)main.FindName("EtymologyButton")).Visibility==Visibility.Visible,
            "restoring a saved view rebuilds IPA, TLDR, word forms and etymology without provider work");
        main.LoadPronunciationAsync("fixture", CancellationToken.None, false, allowNetwork: false).GetAwaiter().GetResult();
        main.LoadPronunciationAsync("fixture", CancellationToken.None, false, allowNetwork: false).GetAwaiter().GetResult();
        var alternate = answer with {Entry = entry with {Phonetic = "/nuː/"}};
        main.RenderAnswers(new([alternate],SenseMerging.Group([alternate])),null);
        Check(((Run)main.FindName("Phonetic")).Text=="/nuː/" && ((Run)main.FindName("PhoneticLabel")).Text.Contains("AI"),
            "repeated cache reads never promote model IPA to dictionary provenance or require a local hit");
        Check(progress.Current is {Busy:false} && ((TextBlock)main.FindName("LookupStatus")).Text.Contains("缓存"),
            "restored query is immediately stable and explicitly marked as cached");
        main.ShowHistory();
    }
}
