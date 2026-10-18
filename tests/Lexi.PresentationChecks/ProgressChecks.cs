using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Lexi;
using Lexi.Core;

internal static partial class Program
{
    private static void CheckQueryProgress(App app, MainWindow main)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
        var view = (QueryProgressView)main.FindName("QueryProgress");
        var entry = (StackPanel)main.FindName("EntryPanel");
        var quick = (Border)main.FindName("QuickReadCard");
        var forms = (StackPanel)main.FindName("FormsPanel");
        Check(view.Visibility == Visibility.Collapsed && entry.Children.IndexOf(quick) < entry.Children.IndexOf(view) &&
            entry.Children.IndexOf(view) < entry.Children.IndexOf(forms) && !Contents((FrameworkElement)main.FindName("QueryBar")).Contains(view),
            "TLDR precedes result progress and late word forms; the search row has no duplicate progress");
        var options = new AppSettings { Provider="codex", Model="gpt-6-luna", ParallelLookup=true, SecondaryProvider="claude", SecondaryModel="sonnet", JudgeProvider="codex", JudgeModel="gpt-6.1-sol" };
        var primary = LookupModel.Primary(options); var secondary = LookupModel.Secondary(options);
        var answers = new[] { new ModelAnswer(primary, ModelPhase.Streaming),new ModelAnswer(secondary, ModelPhase.Pending) };
        var snapshot = new LookupSnapshot(answers, []);
        var request = view.Begin(QueryProgressState.Dictionary(true,true).WithWork(options));
        entry.Visibility = Visibility.Visible; quick.Visibility = Visibility.Visible;
        ((TextBox)main.FindName("QuickReadText")).Text = "幂等性：重复执行同一操作，效果与执行一次相同。";
        Measure(entry, 510, 800);
        var anchor = quick.TranslatePoint(new Point(),entry).Y;
        var waiting = QueryProgressState.Updating(snapshot,true).WithWork(options,snapshot);
        view.Update(request,waiting);
        var marks = Contents(view).OfType<QueryActivityMark>().ToArray();
        Check(marks.Length==3 && marks[0].Activity==QueryActivity.Querying && marks[1].Activity==QueryActivity.Querying && marks[2].Activity==QueryActivity.Idle,
            "result actors use independent soft matrices from the shared work states");
        view.Update(request,waiting with { Detail="new preview text" });
        Check(Contents(view).OfType<QueryActivityMark>().SequenceEqual(marks), "token updates retain actor controls and animation clocks");
        var finishedAnswers = answers.Select(a=>a with { Phase=ModelPhase.Complete }).ToArray();
        var merging = new LookupSnapshot(finishedAnswers,[],Merging:true);
        view.Update(request, QueryProgressState.Updating(merging,true).WithWork(options,merging));
        Measure(entry,510,800);
        Check(quick.TranslatePoint(new Point(),entry).Y==anchor && view.ActualHeight==46 && marks[2].Activity==QueryActivity.Merging &&
            Contents(view).OfType<TextBlock>().Where(t=>t.IsVisible).All(t=>!t.Text.Contains("进行中")),
            "progress occupies one line below a stable TLDR anchor during merging");
        var done = merging with { Merging=false, MergeNote="同义项已整理" };
        view.Update(request, QueryProgressState.Finished(done,true).WithWork(options,done));
        Measure(entry,510,800);
        Check(view.Visibility==Visibility.Collapsed && view.Height==0 && !view.Retiring && quick.TranslatePoint(new Point(),entry).Y==anchor,
            "unloaded or motionless completion releases the whole row without moving TLDR or leaving a done label");
        view.Update(request,waiting);
        Check(view.Current?.Busy==false && view.Visibility==Visibility.Collapsed, "late streaming cannot revive a terminal result row");
        request=view.Begin(QueryProgressState.Finished(done,true).WithWork(options,done));
        Check(view.Visibility==Visibility.Collapsed && !view.Retiring, "restored completed results do not replay shutdown");
        // Native clocks can be sampled without showing a window or driving the desktop.
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        typeof(QueryProgressView).GetMethod("StartRetirement",flags)!.Invoke(view,null);
        var shutdown=(Storyboard)typeof(QueryProgressView).GetField("retirement",flags)!.GetValue(view)!;
        shutdown.SeekAlignedToLastTick(view,TimeSpan.FromMilliseconds(950*.45),TimeSeekOrigin.BeginTime);
        var display=(FrameworkElement)typeof(QueryProgressView).GetField("screen",flags)!.GetValue(view)!;
        var scale=(ScaleTransform)display.RenderTransform;
        var brightLine=(FrameworkElement)typeof(QueryProgressView).GetField("line",flags)!.GetValue(view)!;
        Check(scale.ScaleY<.06 && scale.ScaleX==1 && brightLine.Opacity>.99,
            "native CRT clocks compress vertically into a bright line before horizontal closure");
        shutdown.SeekAlignedToLastTick(view,TimeSpan.FromMilliseconds(950*.78),TimeSeekOrigin.BeginTime);
        var phosphor=(FrameworkElement)typeof(QueryProgressView).GetField("dot",flags)!.GetValue(view)!;
        Check(scale.ScaleX<.01 && phosphor.Opacity>.99, "native CRT clocks end the line in a central phosphor dot");
        var failed = done with { MergeFailed=true, MergeFailure=MergeFailureKind.Timeout };
        view.Begin(QueryProgressState.Finished(failed,true).WithWork(options,failed));
        Measure(entry,510,800);
        Check(view.Visibility==Visibility.Visible && view.Height==46 && !view.Retiring && AutomationProperties.GetName(view).Contains("合并未完成"),
            "a replacement failure cancels old CRT clocks and retains full failure details");
        var retry=false; view.RetryRequested += (_,_)=>retry=true;
        var failedButton=Contents(view).OfType<Button>().Single(b=>AutomationProperties.GetName(b).Contains("响应超时"));
        failedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(retry, "clicking the failed actor requests only the existing failed-step retry path");
        var current=view.Begin(QueryProgressState.Translating().WithWork(options));
        view.Update(request, QueryProgressState.Finished(done,true).WithWork(options,done));
        Check(view.Current?.Translation==true && view.Current.Busy, "a retired query cannot overwrite a new translation");
        view.Update(current,QueryProgressState.Translating().WithWork(options).Interrupted("已停止","已有结果已保留"));
        Check(view.Current?.Warning==true && Contents(view).OfType<QueryActivityMark>().All(m=>m.Activity==QueryActivity.Idle),
            "cancelled translation retains a static warning instead of playing success retirement");
        foreach(var width in new[] { 510d, 720d, 1040d })
        {
            var longOptions=new AppSettings { Model="very-long-provider-name/very-long-model-name-with-capabilities", ParallelLookup=true, SecondaryProvider="claude", SecondaryModel="sonnet" };
            view.Begin(QueryProgressState.Dictionary(true,true).WithWork(longOptions)); Measure(entry,width,800);
            var buttons=Contents(view).OfType<Button>().OrderBy(b=>b.TranslatePoint(new Point(),view).X).ToArray();
            Check(buttons.Length==3 && buttons.All(b=>b.ActualHeight<=36) && buttons.Zip(buttons.Skip(1)).All(pair=>
                pair.First.TranslatePoint(new Point(pair.First.ActualWidth,0),view).X <= pair.Second.TranslatePoint(new Point(),view).X + .1),
                "long model names truncate without overlapping or adding rows at width " + width);
        }
        Check(AutomationProperties.GetLiveSetting(view)==AutomationLiveSetting.Polite,"result progress retains polite accessibility announcements");
        view.Reset(); entry.Visibility=Visibility.Collapsed; quick.Visibility=Visibility.Collapsed;
    }
}
