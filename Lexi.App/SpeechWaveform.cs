using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Lexi;

// The approved soft waveform: five round-ended bars centered in the existing icon box.
// Native animation clocks change transforms only; they never relayout or intercept input.
internal sealed class SpeechWaveform : Viewbox
{
    private static readonly (double Height, int Period, int Offset)[] Profile =
        [(.44,1820,430), (.78,2070,960), (.94,1940,200), (.72,2210,1460), (.43,1890,740)];
    private static readonly (double Time, double Value)[] Frames =
        [(0,.28), (.13,.78), (.27,.40), (.43,1), (.58,.35), (.74,.72), (.88,.24), (1,.28)];
    private readonly ScaleTransform[] scales = new ScaleTransform[5];
    private bool loaded, moving;
    private Window? window;
    public static readonly DependencyProperty IsAnimatingProperty = DependencyProperty.Register(nameof(IsAnimating), typeof(bool),
        typeof(SpeechWaveform), new PropertyMetadata(false, (owner, _) => ((SpeechWaveform)owner).Refresh()));
    public bool IsAnimating { get => (bool)GetValue(IsAnimatingProperty); set => SetValue(IsAnimatingProperty, value); }

    public SpeechWaveform(string brush = "Accent", double size = 18)
    {
        Width = Height = size; Stretch = Stretch.Uniform; IsHitTestVisible = false; Focusable = false;
        var canvas = new Canvas { Width = 18, Height = 18 };
        Child = canvas;
        const double width = 18 * .105, gap = 18 * .075;
        var left = (18 - 5 * width - 4 * gap) / 2;
        for (int i = 0; i < Profile.Length; i++)
        {
            var height = 18 * Profile[i].Height;
            var scale = scales[i] = new ScaleTransform(1, .7);
            var bar = new Rectangle { Width = width, Height = height, RadiusX = width / 2, RadiusY = width / 2,
                RenderTransformOrigin = new(.5,.5), RenderTransform = scale };
            bar.SetResourceReference(Shape.FillProperty, brush);
            Canvas.SetLeft(bar, left + i * (width + gap)); Canvas.SetTop(bar, (18 - height) / 2);
            canvas.Children.Add(bar);
        }
        Loaded += (_, _) =>
        {
            if (loaded) return;
            loaded = true; SystemParameters.StaticPropertyChanged += PreferencesChanged;
            window = Window.GetWindow(this);
            if (window != null) window.StateChanged += WindowStateChanged;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            loaded = false; SystemParameters.StaticPropertyChanged -= PreferencesChanged;
            if (window != null) window.StateChanged -= WindowStateChanged;
            window = null; SetMotion(false);
        };
        IsVisibleChanged += (_, _) => Refresh();
    }
    private void PreferencesChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SystemParameters.ClientAreaAnimation)) return;
        if (Dispatcher.CheckAccess()) Refresh(); else _ = Dispatcher.BeginInvoke(Refresh);
    }
    private void WindowStateChanged(object? sender, EventArgs args) => Refresh();
    private void Refresh() => SetMotion(loaded && IsVisible && IsAnimating && SystemParameters.ClientAreaAnimation && window?.WindowState != WindowState.Minimized);
    internal void SetMotion(bool enabled)
    {
        if (moving == enabled) return;
        moving = enabled;
        for (int i = 0; i < scales.Length; i++)
        {
            if (!enabled) { scales[i].BeginAnimation(ScaleTransform.ScaleYProperty, null); scales[i].ScaleY = .7; continue; }
            var spec = Profile[i];
            var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(spec.Period),
                BeginTime = TimeSpan.FromMilliseconds(-spec.Offset), RepeatBehavior = RepeatBehavior.Forever };
            foreach (var frame in Frames)
                animation.KeyFrames.Add(new SplineDoubleKeyFrame(frame.Value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(frame.Time * spec.Period)), new KeySpline(.4,0,.3,1)));
            Timeline.SetDesiredFrameRate(animation, 30);
            scales[i].BeginAnimation(ScaleTransform.ScaleYProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }
    }
}
