using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Lexi;
using Lexi.Core;

internal static partial class Program
{
    private static void CheckQueryActivity(MainWindow main)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
        var mark = (QueryActivityMark)main.FindName("FooterActivity");
        var button = (Button)main.FindName("HistoryHomeButton");
        Check(ReferenceEquals(mark,button.Content) && !mark.IsHitTestVisible && !mark.Focusable &&
            AutomationProperties.GetName(button)=="返回查询历史", "query animation preserves the history action and its accessible name");
        static IEnumerable<IAnimatable> Targets(QueryActivityMark view)
        {
            foreach (var shape in Contents(view).OfType<Shape>())
            {
                yield return shape;
                if (shape.RenderTransform is IAnimatable transform) yield return transform;
            }
        }
        foreach (var phase in Enum.GetValues<QueryActivity>())
        {
            mark.Activity = phase; Measure(mark,24,24);
            var targets = Targets(mark).ToArray();
            Check(mark.ActualWidth==24 && mark.ActualHeight==24 && targets.All(t=>!t.HasAnimatedProperties),
                $"{phase} activity fits its fixed icon box and is static while unloaded");
            mark.SetMotion(true);
            Check(targets.Any(t=>t.HasAnimatedProperties)==(phase!=QueryActivity.Idle),
                $"{phase} activity uses native animation clocks only while busy");
            mark.SetMotion(false);
            Check(targets.All(t=>!t.HasAnimatedProperties), $"{phase} activity releases its animation clocks when paused");
            mark.SetMotion(true);
            mark.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Check(targets.All(t=>!t.HasAnimatedProperties), $"{phase} activity releases clocks on removal");
        }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var shown = typeof(MainWindow).GetField("shown",flags)!;
        var render = typeof(MainWindow).GetMethod("Render",flags,null,[typeof(QueryView)],null)!;
        QueryView Current() => (QueryView)shown.GetValue(main)!;
        void Publish(QueryProgressState? state)
        {
            var previous = Current();
            render.Invoke(main,[previous with { Revision=previous.Revision+1, Progress=state, ProgressGeneration=previous.ProgressGeneration+1 }]);
        }
        Publish(QueryProgressState.Dictionary(true,true));
        Check(mark.Activity==QueryActivity.Querying, "accepted query progress reaches the footer animation");
        Publish(QueryProgressState.Translating(true));
        Check(mark.Activity==QueryActivity.Generating, "received translation text changes the footer to output motion");
        var stale = Current();
        Publish(QueryProgressState.Translated());
        render.Invoke(main,[stale]);
        Check(mark.Activity==QueryActivity.Idle && Targets(mark).All(t=>!t.HasAnimatedProperties),
            "completion restores the logo and stale streaming views cannot restart its animation");
        Publish(QueryProgressState.Translating().Interrupted("已停止","已保留结果"));
        Check(mark.Activity==QueryActivity.Idle, "failed or cancelled queries stop the footer animation");
        Publish(null);
        Check(mark.Activity==QueryActivity.Idle, "returning home leaves the logo still");
    }
}
