using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Lexi.Core;

namespace Lexi;

// The approved soft matrix, shared by the history button and query-status control.
// Only transforms and opacity move; incoming tokens never restart an unchanged phase.
public sealed class QueryActivityMark : Viewbox
{
    private readonly TextBlock glyph = new() { Text = "ξ", FontFamily = new("Georgia"), FontSize = 19.2,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Canvas query = new() { Width = 24, Height = 24 };
    private readonly Canvas output = new() { Width = 24, Height = 24 };
    private readonly Canvas merge = new() { Width = 24, Height = 24 };
    private readonly List<(QueryActivity Phase, IAnimatable Target, DependencyProperty Property, DoubleAnimationUsingKeyFrames Animation)> clocks = [];
    private bool loaded;
    private QueryActivity moving = QueryActivity.Idle;
    private Window? window;
    public static readonly DependencyProperty ActivityProperty = DependencyProperty.Register(nameof(Activity), typeof(QueryActivity),
        typeof(QueryActivityMark), new PropertyMetadata(QueryActivity.Idle, (owner, _) => ((QueryActivityMark)owner).Refresh()));
    public QueryActivity Activity { get => (QueryActivity)GetValue(ActivityProperty); set => SetValue(ActivityProperty, value); }

    public QueryActivityMark() : this(true) { }

    internal QueryActivityMark(bool framed)
    {
        Width = Height = 24; Stretch = Stretch.Uniform; IsHitTestVisible = false; Focusable = false;
        var grid = new Grid { Width = 24, Height = 24 };
        var frame = new Border { CornerRadius = new(5.28), BorderThickness = new(1) };
        frame.SetResourceReference(Border.BackgroundProperty, "LogoSurface");
        frame.SetResourceReference(Border.BorderBrushProperty, "Accent");
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        if (framed) grid.Children.Add(frame);
        grid.Children.Add(glyph);
        grid.Children.Add(query); grid.Children.Add(output); grid.Children.Add(merge); Child = grid;
        for (int i = 0; i < 9; i++)
        {
            int row = i / 3, col = i % 3;
            var dot = Dot(query, 24 * (.27 + col * .23), 24 * (.27 + row * .23), 2.4, .75);
            var scale = new ScaleTransform(1, 1); dot.RenderTransform = scale; dot.RenderTransformOrigin = new(.5, .5);
            var brightness = new[] { (0d,.26), (.3,1d), (.72,.26), (1d,.26) };
            var size = new[] { (0d,.8), (.3,1.1), (.72,.8), (1d,.8) };
            Register(QueryActivity.Querying, dot, OpacityProperty, brightness, (.4,0,.3,1), (row+col)*.12);
            Register(QueryActivity.Querying, scale, ScaleTransform.ScaleXProperty, size, (.4,0,.3,1), (row+col)*.12);
            Register(QueryActivity.Querying, scale, ScaleTransform.ScaleYProperty, size, (.4,0,.3,1), (row+col)*.12);
        }
        for (int i = 0; i < 3; i++)
        {
            var line = new Rectangle { Width = 24*(.52-i*.07), Height = 24*.07, RadiusX = .84, RadiusY = .84, Opacity = .85 };
            line.SetResourceReference(Shape.FillProperty, "Accent");
            Canvas.SetLeft(line, 24*.24); Canvas.SetTop(line, 24*(.28+i*.22)); output.Children.Add(line);
            var scale = new ScaleTransform(1,1); line.RenderTransform = scale;
            Register(QueryActivity.Generating, scale, ScaleTransform.ScaleXProperty,
                [(0,.32),(.44,1),(.60,1),(1,.32)], (.4,0,.2,1), i*.18);
            Register(QueryActivity.Generating, line, OpacityProperty,
                [(0,.35),(.44,.95),(.60,.95),(1,.35)], (.4,0,.2,1), i*.18);
        }
        for (int i = 0; i < 4; i++)
        {
            double x = i % 2 == 0 ? -1 : 1, y = i < 2 ? -1 : 1;
            var dot = Dot(merge, 12,12,24*.12,.7);
            var position = new TranslateTransform(24*x*.24,24*y*.24); dot.RenderTransform = position;
            Register(QueryActivity.Merging, position, TranslateTransform.XProperty,
                [(0,24*x*.24),(.12,24*x*.24),(.58,24*x*.045),(.72,24*x*.045),(1,24*x*.24)], (.45,0,.25,1));
            Register(QueryActivity.Merging, position, TranslateTransform.YProperty,
                [(0,24*y*.24),(.12,24*y*.24),(.58,24*y*.045),(.72,24*y*.045),(1,24*y*.24)], (.45,0,.25,1));
            Register(QueryActivity.Merging, dot, OpacityProperty,
                [(0,.4),(.12,.4),(.58,.95),(.72,.95),(1,.4)], (.45,0,.25,1));
        }
        var center = Dot(merge,12,12,24*.16,.85);
        var nucleus = new ScaleTransform(1,1); center.RenderTransform = nucleus; center.RenderTransformOrigin = new(.5,.5);
        Register(QueryActivity.Merging, center, OpacityProperty, [(0,.3),(.15,.3),(.58,1),(.72,1),(1,.3)], (.42,0,.58,1));
        foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            Register(QueryActivity.Merging, nucleus, property, [(0,.55),(.15,.55),(.58,1),(.72,1),(1,.55)], (.42,0,.58,1));
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
        Refresh();
    }

    private static Ellipse Dot(Canvas canvas, double x, double y, double size, double opacity)
    {
        var dot = new Ellipse { Width = size, Height = size, Opacity = opacity };
        dot.SetResourceReference(Shape.FillProperty, "Accent");
        Canvas.SetLeft(dot,x-size/2); Canvas.SetTop(dot,y-size/2); canvas.Children.Add(dot); return dot;
    }
    private void Register(QueryActivity phase, IAnimatable target, DependencyProperty property,
        (double Time,double Value)[] frames, (double X1,double Y1,double X2,double Y2) easing, double offset = 0)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(2.2),
            BeginTime = TimeSpan.FromSeconds(-2.2*offset), RepeatBehavior = RepeatBehavior.Forever };
        foreach (var frame in frames)
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(frame.Value, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.2*frame.Time)),
                new KeySpline(easing.X1,easing.Y1,easing.X2,easing.Y2)));
        Timeline.SetDesiredFrameRate(animation,30); animation.Freeze(); clocks.Add((phase,target,property,animation));
    }
    private void PreferencesChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SystemParameters.ClientAreaAnimation)) return;
        if (Dispatcher.CheckAccess()) Refresh(); else _ = Dispatcher.BeginInvoke(Refresh);
    }
    private void WindowStateChanged(object? sender, EventArgs args) => Refresh();
    private void Refresh()
    {
        glyph.Visibility = Activity == QueryActivity.Idle ? Visibility.Visible : Visibility.Collapsed;
        query.Visibility = Activity == QueryActivity.Querying ? Visibility.Visible : Visibility.Collapsed;
        output.Visibility = Activity == QueryActivity.Generating ? Visibility.Visible : Visibility.Collapsed;
        merge.Visibility = Activity == QueryActivity.Merging ? Visibility.Visible : Visibility.Collapsed;
        SetMotion(loaded && IsVisible && SystemParameters.ClientAreaAnimation && window?.WindowState != WindowState.Minimized);
    }
    internal void SetMotion(bool enabled)
    {
        var phase = enabled ? Activity : QueryActivity.Idle;
        if (moving == phase) return;
        foreach (var clock in clocks.Where(c => c.Phase == moving)) clock.Target.BeginAnimation(clock.Property,null);
        moving = phase;
        foreach (var clock in clocks.Where(c => c.Phase == moving))
            clock.Target.BeginAnimation(clock.Property,clock.Animation,HandoffBehavior.SnapshotAndReplace);
    }
}
