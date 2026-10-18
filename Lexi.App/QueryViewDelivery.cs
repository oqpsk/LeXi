using System.Windows.Threading;
using Lexi.Core;

namespace Lexi;

// One pending render, always the newest revision. Input, scrolling and cancellation
// run before background-priority view delivery. No UI controls are touched by producers.
internal sealed class QueryViewDelivery(Dispatcher dispatcher, Action<QueryView> render)
{
    private readonly object gate = new();
    private QueryView? pending;
    private bool queued;
    public void Post(QueryView view)
    {
        lock (gate)
        {
            if (dispatcher.HasShutdownStarted) return;
            if (pending == null || view.Revision > pending.Revision) pending = view;
            if (queued) return;
            queued = true;
        }
        _ = dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            QueryView? latest;
            lock (gate) { latest = pending; pending = null; queued = false; }
            if (latest != null) render(latest);
        }));
    }
}
