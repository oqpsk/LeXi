using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Lexi.Core;
using ShapePath = System.Windows.Shapes.Path;

namespace Lexi;

public sealed class QueryProgressView : Button
{
    private readonly TextBlock title = new() { FontSize = 14, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock detail = new() { FontSize = 13, Margin = new(0, 5, 0, 8), MaxWidth = 320, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock stages = new() { FontSize = 12 };
    private readonly Ellipse ring = new() { Width = 18, Height = 18, StrokeThickness = 1.5 };
    private readonly QueryActivityMark activity = new(false);
    private readonly ShapePath symbol = new() { Width = 18, Height = 18, StrokeThickness = 1.5,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
    private readonly ToolTip information = new();
    private readonly QueryProgressTracker tracker = new();
    internal QueryProgressState? Current => tracker.Current;

    protected override AutomationPeer OnCreateAutomationPeer() => new ButtonAutomationPeer(this);

    public QueryProgressView()
    {
        Visibility = Visibility.Hidden;
        Style = (Style)Application.Current.FindResource("IconButton");
        var glyph = new Grid { Width = 24, Height = 24, IsHitTestVisible = false };
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
        Unloaded += (_, _) => information.IsOpen = false;
    }

    internal void Reset()
    { tracker.Reset(); activity.Activity = QueryActivity.Idle; Visibility = Visibility.Hidden; information.IsOpen = false; }

    internal long Begin(QueryProgressState state)
    {
        var request = tracker.Begin(state);
        Show(state); return request;
    }

    internal void Update(long request, QueryProgressState state)
    { if (tracker.Update(request, state)) Show(state); }

    private void Show(QueryProgressState state)
    {
        Visibility = Visibility.Visible;
        title.Text = state.Title; detail.Text = state.Detail;
        var color = Theme.Brush(state.Warning ? "Warning" : "Accent"); title.Foreground = color;
        ring.Stroke = color; ring.Opacity = 0.65; ring.Visibility = state.Busy ? Visibility.Collapsed : Visibility.Visible;
        activity.Activity = state.Activity; activity.Visibility = state.Busy ? Visibility.Visible : Visibility.Collapsed;
        symbol.Stroke = color; symbol.Visibility = state.Busy ? Visibility.Collapsed : Visibility.Visible;
        symbol.Data = Geometry.Parse(state.Warning ? "M 9,4 L 9,10 M 9,13 L 9,13.1" : "M 5,9 L 8,12 L 13,6");
        stages.Text = string.Join("\n", state.StageLabels);
        AutomationProperties.SetName(this, state.AccessibleName);
        if (UIElementAutomationPeer.FromElement(this) is { } peer) peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
