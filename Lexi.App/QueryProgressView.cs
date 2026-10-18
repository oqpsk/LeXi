using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Lexi.Core;
using ShapePath = System.Windows.Shapes.Path;

namespace Lexi;

// A single line below TL;DR. Core supplies actor states; retirement is local presentation state.
public sealed class QueryProgressView : Grid
{
    private readonly QueryProgressTracker tracker = new();
    private readonly Grid screen = new() { Height = 36, VerticalAlignment = VerticalAlignment.Top };
    private readonly Grid slots = new();
    private readonly ScaleTransform screenScale = new(1, 1), lineScale = new(1, 1), dotScale = new(1, 1);
    private readonly Rectangle line = new() { Height = 2, Opacity = 0, RadiusX = 1, RadiusY = 1, IsHitTestVisible = false };
    private readonly Ellipse dot = new() { Width = 4, Height = 4, Opacity = 0, IsHitTestVisible = false };
    private readonly TextBlock title = new() { FontSize = 14, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock detail = new() { FontSize = 13, Margin = new(0, 5, 0, 8), MaxWidth = 360, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock stages = new() { FontSize = 12 };
    private readonly ToolTip information = new();
    private readonly List<Slot> actors = [];
    private Storyboard? retirement;
    private bool liveSeen;
    private long visualGeneration;
    private string announcement = "";
    private Window? window;
    internal QueryProgressState? Current => tracker.Current;
    internal event EventHandler? RetryRequested;
    internal bool Retiring => retirement != null;

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    public QueryProgressView()
    {
        Visibility = Visibility.Collapsed; ClipToBounds = true; Focusable = false;
        var background = new Border { CornerRadius = new(7), Child = slots };
        background.SetResourceReference(Border.BackgroundProperty, "PanelSurface");
        screen.Children.Add(background); screen.RenderTransform = screenScale; screen.RenderTransformOrigin = new(.5, .5);
        var visual = new Grid { Height = 36, VerticalAlignment = VerticalAlignment.Top };
        visual.Children.Add(screen); visual.Children.Add(line); visual.Children.Add(dot); Children.Add(visual);
        line.RenderTransform = lineScale; line.RenderTransformOrigin = new(.5, .5);
        dot.RenderTransform = dotScale; dot.RenderTransformOrigin = new(.5, .5);
        var accent = ((SolidColorBrush)Theme.Brush("Accent")).Color;
        var core = Color.FromRgb(255, 245, 250);
        line.Fill = new LinearGradientBrush(new GradientStopCollection { new(Colors.Transparent, 0), new(accent, .09), new(core, .5), new(accent, .91), new(Colors.Transparent, 1) }, 0);
        dot.Fill = new SolidColorBrush(core);
        foreach (var shape in new Shape[] { line, dot })
            shape.Effect = new DropShadowEffect { Color = accent, ShadowDepth = 0, BlurRadius = shape == line ? 10 : 14, Opacity = .7 };
        var body = new StackPanel(); body.Children.Add(title); body.Children.Add(detail); body.Children.Add(stages);
        information.Content = body; ToolTip = information;
        detail.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); stages.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        Loaded += (_, _) =>
        {
            window = Window.GetWindow(this);
            if (window != null) window.StateChanged += WindowChanged;
            SystemParameters.StaticPropertyChanged += PreferencesChanged;
        };
        Unloaded += (_, _) =>
        {
            if (window != null) window.StateChanged -= WindowChanged;
            window = null; SystemParameters.StaticPropertyChanged -= PreferencesChanged;
            StopRetirement(); if (Current?.Outcome == QueryOutcome.Complete) Collapse();
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible && retirement != null) { StopRetirement(); Collapse(); } };
    }

    internal void Reset()
    { tracker.Reset(); liveSeen = false; visualGeneration++; StopRetirement(); Collapse(); information.IsOpen = false; }

    internal long Begin(QueryProgressState state)
    {
        visualGeneration++; StopRetirement(); liveSeen = false;
        var request = tracker.Begin(state); Show(state); return request;
    }

    internal void Update(long request, QueryProgressState state)
    { if (tracker.Update(request, state)) Show(state); }

    private void Show(QueryProgressState state)
    {
        title.Text = state.Title; detail.Text = state.Detail; stages.Text = string.Join("\n", state.StageLabels);
        title.SetResourceReference(TextBlock.ForegroundProperty, state.Warning ? "Warning" : "Accent");
        // Slots survive tokens, preserving animation clocks and keyboard focus.
        while (actors.Count > state.Items.Length) { slots.Children.Remove(actors[^1].Button); actors.RemoveAt(actors.Count - 1); }
        while (actors.Count < state.Items.Length)
        {
            var slot = new Slot(); actors.Add(slot); slots.Children.Add(slot.Button);
            slot.Button.Click += (_, _) => { if (slot.Item?.State == QueryWorkState.Failed) RetryRequested?.Invoke(this, EventArgs.Empty); else information.IsOpen = !information.IsOpen; };
        }
        if (slots.ColumnDefinitions.Count != actors.Count)
        { slots.ColumnDefinitions.Clear(); foreach (var _ in actors) slots.ColumnDefinitions.Add(new()); }
        for (var i = 0; i < actors.Count; i++) { Grid.SetColumn(actors[i].Button, i); actors[i].Show(state.Items[i]); }
        AutomationProperties.SetName(this, state.AccessibleName + "。" + string.Join("，", state.Items.Select(i => i.AccessibleName)));
        var phase = state.Outcome + ":" + state.Stage + ":" + string.Join(",", state.Items.Select(i => i.Key + i.State));
        if (phase != announcement)
        {
            announcement = phase;
            if (UIElementAutomationPeer.FromElement(this) is { } peer) peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        if (state.Busy)
        {
            liveSeen |= state.Items.Any(i => i.Activity != QueryActivity.Idle);
            Visibility = Visibility.Visible; Height = 46; return;
        }
        if (state.Warning) { StopRetirement(); Visibility = Visibility.Visible; Height = 46; return; }
        if (!state.Restored && liveSeen && IsLoaded && IsVisible && SystemParameters.ClientAreaAnimation && window?.WindowState != WindowState.Minimized)
            StartRetirement();
        else Collapse();
    }

    private void StartRetirement()
    {
        StopRetirement();
        var generation = visualGeneration;
        retirement = new Storyboard();
        AddScale(screen, ScaleTransform.ScaleYProperty, [(0,1),(.10,1),(.37,.055),(.51,.04),(.76,.04),(1,.04)]);
        AddScale(screen, ScaleTransform.ScaleXProperty, [(0,1),(.51,1),(.76,.004),(1,.004)]);
        Add(screen, OpacityProperty, [(0,1),(.37,1),(.51,.7),(.76,0),(1,0)]);
        AddScale(line, ScaleTransform.ScaleXProperty, [(0,1),(.51,1),(.76,.005),(.81,0),(1,0)]);
        Add(line, OpacityProperty, [(0,0),(.27,0),(.37,1),(.76,1),(.81,0),(1,0)]);
        AddScale(dot, ScaleTransform.ScaleXProperty, [(0,.6),(.70,.6),(.78,1.25),(.88,.7),(1,0)]);
        AddScale(dot, ScaleTransform.ScaleYProperty, [(0,.6),(.70,.6),(.78,1.25),(.88,.7),(1,0)]);
        Add(dot, OpacityProperty, [(0,0),(.70,0),(.78,1),(.88,.6),(1,0)]);
        retirement.Completed += (_, _) =>
        {
            if (generation != visualGeneration || Current?.Outcome != QueryOutcome.Complete) return;
            var collapse = new DoubleAnimation(46, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            collapse.Completed += (_, _) => { if (generation == visualGeneration && Current?.Outcome == QueryOutcome.Complete) { StopRetirement(); Collapse(); } };
            BeginAnimation(HeightProperty, collapse);
        };
        retirement.Begin(this, true);
    }

    private void Add(DependencyObject target, DependencyProperty property, (double Time, double Value)[] frames)
        => AddPath(target, new PropertyPath(property), frames);
    private void AddScale(FrameworkElement target, DependencyProperty property, (double Time, double Value)[] frames)
        => AddPath(target, new PropertyPath("(0).(1)", RenderTransformProperty, property), frames);
    private void AddPath(DependencyObject target, PropertyPath path, (double Time, double Value)[] frames)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(950) };
        foreach (var frame in frames) animation.KeyFrames.Add(new SplineDoubleKeyFrame(frame.Value,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(950 * frame.Time)), new KeySpline(.7, 0, .85, .5)));
        Timeline.SetDesiredFrameRate(animation, 30);
        Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, path); retirement!.Children.Add(animation);
    }

    private void StopRetirement()
    {
        retirement?.Remove(this); retirement = null; BeginAnimation(HeightProperty, null);
        screen.RenderTransform = new ScaleTransform(1, 1); line.RenderTransform = new ScaleTransform(1, 1); dot.RenderTransform = new ScaleTransform(1, 1);
        screen.Opacity = 1; line.Opacity = dot.Opacity = 0;
    }
    private void Collapse() { Visibility = Visibility.Collapsed; Height = 0; }
    private void WindowChanged(object? sender, EventArgs e) { if (window?.WindowState == WindowState.Minimized && retirement != null) { StopRetirement(); Collapse(); } }
    private void PreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SystemParameters.ClientAreaAnimation)) return;
        void Update() { if (!SystemParameters.ClientAreaAnimation && retirement != null) { StopRetirement(); Collapse(); } }
        if (Dispatcher.CheckAccess()) Update(); else _ = Dispatcher.BeginInvoke(Update);
    }

    private sealed class Slot
    {
        public readonly Button Button = new() { Height = 34, Background = Brushes.Transparent, BorderThickness = new(0), Padding = new(7, 0, 7, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        private readonly QueryActivityMark motion = new(false) { Width = 20, Height = 20 };
        private readonly ShapePath symbol = new() { Width = 18, Height = 18, Stretch = Stretch.Uniform, StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        private readonly TextBlock text = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        public QueryWorkItem? Item;
        public Slot()
        {
            var content = new Grid(); content.ColumnDefinitions.Add(new() { Width = new GridLength(20) }); content.ColumnDefinitions.Add(new());
            var icon = new Grid { Width = 20, Height = 20 }; icon.Children.Add(motion); icon.Children.Add(symbol);
            content.Children.Add(icon); Grid.SetColumn(text, 1); text.Margin = new(7, 0, 0, 0); content.Children.Add(text); Button.Content = content;
        }
        public void Show(QueryWorkItem item)
        {
            Item = item; text.Text = item.Name;
            text.SetResourceReference(TextBlock.ForegroundProperty, item.State == QueryWorkState.Failed ? "Warning" : item.Activity == QueryActivity.Idle ? "Muted" : "Ink");
            motion.Activity = item.Activity; motion.Visibility = item.Activity == QueryActivity.Idle ? Visibility.Collapsed : Visibility.Visible;
            symbol.Visibility = item.Activity == QueryActivity.Idle ? Visibility.Visible : Visibility.Collapsed;
            symbol.SetResourceReference(Shape.StrokeProperty, item.State == QueryWorkState.Failed ? "Warning" : "Muted");
            symbol.Data = Geometry.Parse(item.State switch
            { QueryWorkState.Complete => "M 2,9 L 6,13 L 15,3", QueryWorkState.Failed => "M 9,1 A 8,8 0 1 1 8.99,1 M 9,4 L 9,10 M 9,13 L 9,13.1", _ => "M 3,9 L 3,9.2 M 9,9 L 9,9.2 M 15,9 L 15,9.2" });
            Button.ToolTip = item.AccessibleName + (item.State == QueryWorkState.Failed ? " · 点击仅重试未完成步骤" : " · 点击查看阶段详情");
            AutomationProperties.SetName(Button, item.AccessibleName);
        }
    }
}
