using System.Reflection;
using System.Windows.Threading;
using Lexi;
using Lexi.Core;

internal static partial class Program
{
    private static void CheckStreamingDispatch(MainWindow main)
    {
        var initial = (QueryView)typeof(MainWindow).GetField("shown", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Pump only a dedicated private dispatcher: no Application startup, HWND, window
        // activation or desktop input. Exercise the same delivery object used by MainWindow.
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var seen = initial; int posted = 0;
                dispatcher.Hooks.OperationPosted += (_, e) => { if (e.Operation.Priority == DispatcherPriority.Background) posted++; };
                var delivery = new QueryViewDelivery(dispatcher, view => seen = view);
                Task.Run(() =>
                {
                    for (int i = 1; i <= 10000; i++) delivery.Post(initial with { Revision = initial.Revision + i, Status = "preview" + i });
                    delivery.Post(initial with { Revision = initial.Revision + 1, Status = "stale" });
                }).GetAwaiter().GetResult();
                if (posted != 1) throw new Exception("Token bursts must schedule one pending UI refresh.");
                Console.WriteLine("PASS: ten thousand streaming views schedule only one background UI refresh.");
                bool inputFirst = false;
                _ = dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => inputFirst = ReferenceEquals(seen, initial)));
                var frame = new DispatcherFrame();
                _ = dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                if (!inputFirst || seen.Status != "preview10000") throw new Exception("Input must run ahead of rendering and only the newest revision should render.");
                Console.WriteLine("PASS: input gets priority over preview rendering and stale revisions cannot replace the newest pending view.");
                finished.TrySetResult();
            }
            catch (Exception e) { finished.TrySetException(e); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        finished.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
    }
}
