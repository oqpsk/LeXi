using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Lexi;

public sealed class QueryProgressView : Button
{
    private readonly TextBlock title = new() { FontSize = 14, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock detail = new() { FontSize = 13, Margin = new(0, 5, 0, 8), MaxWidth = 320, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock stages = new() { FontSize = 12 };
    private readonly Ellipse[] dots = Enumerable.Range(0, 4).Select(_ => new Ellipse { Width = 7, Height = 7, StrokeThickness = 1 }).ToArray();
    private readonly ToolTip information = new();
    private long generation;
    internal QueryProgressState? Current { get; private set; }

    protected override AutomationPeer OnCreateAutomationPeer() => new ButtonAutomationPeer(this);

    public QueryProgressView()
    {
        Visibility = Visibility.Hidden;
        Style = (Style)Application.Current.FindResource("IconButton");
        Width = 32; Height = 32; Padding = new(0);
        var diamond = new Canvas { Width = 27, Height = 27, IsHitTestVisible = false };
        var positions = new[] { (10.0, 0.0), (20.0, 10.0), (10.0, 20.0), (0.0, 10.0) };
        for (int i = 0; i < dots.Length; i++)
        { Canvas.SetLeft(dots[i], positions[i].Item1); Canvas.SetTop(dots[i], positions[i].Item2); diamond.Children.Add(dots[i]); }
        Content = diamond;
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

    internal long Begin(QueryProgressState state)
    { generation++; Current = null; Update(generation, state); return generation; }

    internal void Update(long request, QueryProgressState state)
    {
        if (request != generation || Current is { Busy: false } || Current is { } previous && state.Stage < previous.Stage) return;
        if (state == Current) return;
        Current = state; Visibility = Visibility.Visible;
        title.Text = state.Title; detail.Text = state.Detail;
        var color = Theme.Brush(state.Warning ? "Warning" : "Accent"); title.Foreground = color;
        var names = new[] { "词典", state.Translation ? "翻译" : "补充", "合并", "完成" };
        var labels = new List<string>();
        for (int i = 0; i < dots.Length; i++)
        {
            bool skipped = i == 0 && state.Translation || i == 1 && !state.ModelsEnabled || i == 2 && !state.MergeEnabled;
            bool active = i == (int)state.Stage;
            dots[i].Fill = skipped || i > (int)state.Stage ? Brushes.Transparent : active ? color : Theme.Brush("Accent");
            dots[i].Stroke = skipped ? Theme.Brush("Stroke") : active ? color : Theme.Brush("Muted");
            var label = skipped ? "跳过" : active ? state.Busy ? "进行中" : state.Warning ? "已结束" : "完成" : i < (int)state.Stage ? "已结束" : "等待";
            labels.Add(names[i] + "：" + label);
        }
        stages.Text = string.Join("\n", labels);
        AutomationProperties.SetName(this, state.Title + "。" + state.Detail + "。" + string.Join("，", labels));
        if (UIElementAutomationPeer.FromElement(this) is { } peer) peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        Animate();
    }

    private void StopAnimation()
    { foreach (var dot in dots) { dot.BeginAnimation(OpacityProperty, null); dot.Opacity = 1; } }
    private void Animate()
    {
        StopAnimation();
        if (Current is { Busy: true } state && IsLoaded && SystemParameters.ClientAreaAnimation)
            dots[(int)state.Stage].BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromSeconds(0.65))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
    }
}
