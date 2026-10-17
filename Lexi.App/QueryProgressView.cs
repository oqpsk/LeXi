using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ShapePath = System.Windows.Shapes.Path;

namespace Lexi;

public sealed class QueryProgressView : Button
{
    private readonly TextBlock title = new() { FontSize = 14, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock detail = new() { FontSize = 13, Margin = new(0, 5, 0, 8), MaxWidth = 320, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock stages = new() { FontSize = 12 };
    private readonly Ellipse ring = new() { Width = 18, Height = 18, StrokeThickness = 1.5 };
    private readonly ShapePath activity = new() { Width = 18, Height = 18, StrokeThickness = 1.5,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        Data = Geometry.Parse("M 9,0.75 A 8.25,8.25 0 0 1 17.25,9"), RenderTransform = new RotateTransform(0, 9, 9) };
    private readonly ShapePath symbol = new() { Width = 18, Height = 18, StrokeThickness = 1.5,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
    private readonly ToolTip information = new();
    private long generation;
    internal QueryProgressState? Current { get; private set; }

    protected override AutomationPeer OnCreateAutomationPeer() => new ButtonAutomationPeer(this);

    public QueryProgressView()
    {
        Visibility = Visibility.Hidden;
        Style = (Style)Application.Current.FindResource("IconButton");
        var glyph = new Grid { Width = 18, Height = 18, IsHitTestVisible = false };
        glyph.Children.Add(ring); glyph.Children.Add(activity); glyph.Children.Add(symbol);
        Content = glyph;
        var body = new StackPanel(); body.Children.Add(title); body.Children.Add(detail); body.Children.Add(stages);
        information.Content = body; ToolTip = information;
        detail.Foreground = Theme.Brush("Muted"); stages.Foreground = Theme.Brush("Muted");
        ToolTipService.SetInitialShowDelay(this, 150); ToolTipService.SetShowDuration(this, 60000);
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        Click += (_, _) => information.IsOpen = !information.IsOpen;
        GotKeyboardFocus += (_, _) => information.IsOpen = true;
        LostKeyboardFocus += (_, _) => information.IsOpen = false;
        Loaded += (_, _) => Animate();
        Unloaded += (_, _) => { information.IsOpen = false; StopAnimation(); };
    }

    internal void Reset()
    { generation++; Current = null; Visibility = Visibility.Hidden; information.IsOpen = false; StopAnimation(); }

    internal long Begin(QueryProgressState state)
    { generation++; Current = null; Update(generation, state); return generation; }

    internal void Update(long request, QueryProgressState state)
    {
        if (request != generation || Current is { Busy: false } || Current is { } previous && state.Stage < previous.Stage) return;
        if (state == Current) return;
        Current = state; Visibility = Visibility.Visible;
        title.Text = state.Title; detail.Text = state.Detail;
        var color = Theme.Brush(state.Warning ? "Warning" : "Accent"); title.Foreground = color;
        ring.Stroke = state.Busy ? Theme.Brush("Muted") : color; ring.Opacity = state.Busy ? 0.3 : 0.65;
        activity.Stroke = color; activity.Visibility = state.Busy ? Visibility.Visible : Visibility.Collapsed;
        symbol.Stroke = color; symbol.Visibility = state.Busy ? Visibility.Collapsed : Visibility.Visible;
        symbol.Data = Geometry.Parse(state.Warning ? "M 9,4 L 9,10 M 9,13 L 9,13.1" : "M 5,9 L 8,12 L 13,6");
        var names = new[] { "词典", state.Translation ? "翻译" : "补充", "合并", "完成" };
        var labels = new List<string>();
        for (int i = 0; i < names.Length; i++)
        {
            bool skipped = i == 0 && state.Translation || i == 1 && !state.ModelsEnabled || i == 2 && !state.MergeEnabled;
            bool active = i == (int)state.Stage;
            var label = skipped ? "跳过" : active ? state.Busy ? "进行中" : state.Warning ? "已结束" : "完成" : i < (int)state.Stage ? "已结束" : "等待";
            labels.Add(names[i] + "：" + label);
        }
        stages.Text = string.Join("\n", labels);
        AutomationProperties.SetName(this, state.Title + "。" + state.Detail + "。" + string.Join("，", labels));
        if (UIElementAutomationPeer.FromElement(this) is { } peer) peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        Animate();
    }

    private void StopAnimation()
    { var rotation = (RotateTransform)activity.RenderTransform; rotation.BeginAnimation(RotateTransform.AngleProperty, null); rotation.Angle = 0; }
    private void Animate()
    {
        StopAnimation();
        if (Current is { Busy: true } && IsLoaded && SystemParameters.ClientAreaAnimation)
            ((RotateTransform)activity.RenderTransform).BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
    }
}
