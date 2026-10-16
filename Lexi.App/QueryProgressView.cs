using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Lexi;

public sealed class QueryProgressView : Border
{
    private readonly TextBlock title = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock detail = new() { FontSize = 13, TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock steps = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
    private readonly Path icon = new() { Width = 14, Height = 14, Stretch = Stretch.Uniform, StrokeThickness = 1.7,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransformOrigin = new(0.5, 0.5) };
    private readonly RotateTransform rotation = new();
    private long generation;
    internal QueryProgressState? Current { get; private set; }

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    public QueryProgressView()
    {
        Visibility = Visibility.Collapsed;
        Padding = new(22, 7, 22, 7);
        BorderBrush = Theme.Brush("Stroke"); BorderThickness = new(0, 0, 0, 1);
        Background = Theme.Brush("PanelSurface");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new() { Width = new(23) });
        grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = new(20) }); grid.RowDefinitions.Add(new() { Height = new(19) });
        icon.RenderTransform = rotation; icon.HorizontalAlignment = HorizontalAlignment.Left; icon.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 1); Grid.SetColumn(steps, 2); Grid.SetColumn(detail, 1); Grid.SetRow(detail, 1); Grid.SetColumnSpan(detail, 2);
        detail.Foreground = Theme.Brush("Muted"); steps.Margin = new(12, 0, 0, 0);
        grid.Children.Add(icon); grid.Children.Add(title); grid.Children.Add(steps); grid.Children.Add(detail); Child = grid;
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        Loaded += (_, _) => Animate();
        Unloaded += (_, _) => rotation.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    internal long Begin(QueryProgressState state)
    {
        generation++; Current = null; Update(generation, state); return generation;
    }

    internal void Update(long request, QueryProgressState state)
    {
        // Old callbacks and already-finished requests must never revive the spinner.
        if (request != generation || Current is { Busy: false } || Current is { } previous && state.Stage < previous.Stage) return;
        if (state == Current) return;
        Current = state; Visibility = Visibility.Visible;
        title.Text = state.Title; detail.Text = state.Detail;
        var color = Theme.Brush(state.Warning ? "Warning" : "Accent");
        title.Foreground = color; icon.Stroke = color;
        icon.Data = Geometry.Parse(state.Busy ? "M 7,1 A 6,6 0 1 1 1,7" : state.Warning
            ? "M 7,1 L 13,13 L 1,13 Z M 7,5 L 7,8 M 7,10 L 7,10.5" : "M 1,7 L 5,11 L 13,2");
        steps.Inlines.Clear();
        var stages = state.Translation ? new[] { (QueryStage.Supplement, "翻译"), (QueryStage.Finished, "完成") }
            : new[] { (QueryStage.Dictionary, "词典"), (QueryStage.Supplement, "补充"), (QueryStage.Merge, "合并"), (QueryStage.Finished, "完成") };
        foreach (var (stage, label) in stages)
        {
            if (steps.Inlines.Count > 0) steps.Inlines.Add(new Run(" · ") { Foreground = Theme.Brush("Muted") });
            var skipped = stage == QueryStage.Supplement && !state.ModelsEnabled || stage == QueryStage.Merge && !state.MergeEnabled;
            steps.Inlines.Add(new Run(stage == QueryStage.Finished && state.Warning ? "结束" : label)
            {
                Foreground = !skipped && stage == state.Stage ? color : Theme.Brush("Muted"),
                TextDecorations = skipped ? TextDecorations.Strikethrough : null,
                ToolTip = skipped ? "本次查询跳过此阶段" : label
            });
        }
        var announcement = state.Title + "。" + state.Detail;
        AutomationProperties.SetName(this, announcement);
        if (UIElementAutomationPeer.FromElement(this) is { } peer) peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        Animate();
    }

    private void Animate()
    {
        rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        rotation.Angle = 0;
        if (Current is { Busy: true } && IsLoaded && SystemParameters.ClientAreaAnimation)
            rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
    }
}
