using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Lexi;
using Lexi.Core;

internal static partial class Program
{
    private static void CheckSpeechWaveform(MainWindow main)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
        foreach (var size in new[] { 14d, 18d })
        {
            var wave = new SpeechWaveform(size: size);
            Measure(wave, size, size);
            var bars = ((Canvas)wave.Child).Children.OfType<Rectangle>().ToArray();
            var scales = bars.Select(b => (ScaleTransform)b.RenderTransform).ToArray();
            Check(wave.Width == size && wave.Height == size && !wave.IsHitTestVisible && !wave.Focusable &&
                bars.Length == 5 && bars.All(b => b.RadiusX == b.Width / 2 && b.RenderTransformOrigin == new Point(.5,.5)),
                $"{size} DIP waveform has five round bars and leaves button input intact");
            Check(bars.All(b => Math.Abs(Canvas.GetTop(b) + b.Height / 2 - 9) < .001) &&
                scales.All(s => s.ScaleY == .7 && !s.HasAnimatedProperties),
                $"{size} DIP preparation waveform stays still and vertically centered");
            foreach (var dpi in new[] { 1d, 1.5d })
            {
                var image = new RenderTargetBitmap((int)Math.Ceiling(size*dpi), (int)Math.Ceiling(size*dpi), 96*dpi,96*dpi,PixelFormats.Pbgra32);
                image.Render(wave);
                var pixels = new byte[image.PixelWidth*image.PixelHeight*4]; image.CopyPixels(pixels,image.PixelWidth*4,0);
                var occupied = Enumerable.Range(0,image.PixelHeight).Where(y => Enumerable.Range(0,image.PixelWidth).Any(x => pixels[(y*image.PixelWidth+x)*4+3]>64)).ToArray();
                Check(occupied.Length > 2 && Math.Abs((occupied[0]+occupied[^1]+1)/(2*dpi)-size/2)<=.6,
                    $"{size} DIP waveform pixels remain centered at {dpi*100}% scale");
            }
            wave.SetMotion(true);
            Check(scales.All(s => s.HasAnimatedProperties), $"{size} DIP playback attaches native animation clocks without UI polling");
            wave.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Check(scales.All(s => !s.HasAnimatedProperties && s.ScaleY == .7), $"{size} DIP removal releases all animation clocks");
        }

        var sense = new AiSense("noun", "A synthetic definition.", "用于检查的释义。", "", "A synthetic example.", "用于检查的例句。", []);
        var answers = new[] { new ModelAnswer(new("codex", "gpt-6.1-sol"), ModelPhase.Complete,
            new("synthetic", true, [sense], "", "source_based")) };
        ((FrameworkElement)main.FindName("EntryPanel")).Visibility = Visibility.Visible;
        main.RenderAnswers(new LookupSnapshot(answers, SenseMerging.Group(answers,new("fixture",[]))), null);
        Measure((FrameworkElement)main.Content,718,618);
        var panel = (Panel)main.FindName("AiPanel");
        var text = Contents(panel).OfType<TextBox>().First(t => t.Text == sense.Chinese);
        var row = (Grid)((Border)text.Parent).Parent;
        var speaker = row.Children.OfType<Button>().Single();
        var heading = (Button)main.FindName("SpeechButton");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void State(string? key, bool playing)
        {
            typeof(MainWindow).GetField("speakingKey",flags)!.SetValue(main,key);
            typeof(MainWindow).GetField("audioPlaying",flags)!.SetValue(main,playing);
            typeof(MainWindow).GetMethod("ShowSpeechVisuals",flags)!.Invoke(main,null);
        }
        State((string)row.Tag,false);
        var inline = speaker.Content as SpeechWaveform;
        Check(inline is { Width: 14, IsAnimating: false } && speaker.Visibility == Visibility.Visible &&
            AutomationProperties.GetName(speaker) == "停止朗读", "preparing a definition uses a static waveform with a stop action");
        State((string)row.Tag,true);
        row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) { RoutedEvent = Mouse.MouseEnterEvent });
        row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) { RoutedEvent = Mouse.MouseLeaveEvent });
        Check(ReferenceEquals(inline,speaker.Content) && inline!.IsAnimating && speaker.Visibility == Visibility.Visible,
            "the playing waveform stays visible and keeps its identity through pointer movement");
        State(MainWindow.HeadingSpeech,true);
        Check(heading.Content is SpeechWaveform { Width: 18, IsAnimating: true } && speaker.Content is System.Windows.Shapes.Path &&
            AutomationProperties.GetName(heading) == "停止合成发音", "switching to the headword moves playback to its 18 DIP waveform only");
        heading.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(heading.Content is System.Windows.Shapes.Path && AutomationProperties.GetName(heading) == "播放或停止合成发音" &&
            speaker.Visibility == Visibility.Hidden, "clicking the waveform restores speakers and stops its request without generating audio");
    }
}
