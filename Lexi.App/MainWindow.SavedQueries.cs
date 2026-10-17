using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    internal void RenderSavedDictionary(SavedDictionary saved, long request, LookupTiming timing)
    {
        var state = session.RestoreSaved(saved, timing);
        QueryProgress.Update(request, state);
    }
}
