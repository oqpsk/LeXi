namespace Lexi.Core;

public sealed record QueryPage(string Query, bool Translation = false, double ScrollOffset = 0);

/// <summary>Session navigation, independent of persisted recent-query history.</summary>
public sealed class QueryNavigation
{
    public const int Capacity = 100;
    private readonly List<QueryPage> pages = [new("")];
    private int index;
    public QueryPage Current => pages[index];
    public QueryPage? Back => CanBack ? pages[index - 1] : null;
    public QueryPage? Forward => CanForward ? pages[index + 1] : null;
    public bool CanBack => index > 0;
    public bool CanForward => index < pages.Count - 1;
    public long Revision { get; private set; }

    public void RememberScroll(double offset)
    { pages[index] = Current with { ScrollOffset = double.IsFinite(offset) ? Math.Max(0, offset) : 0 }; }

    public void Visit(string query, bool translation = false)
    {
        Revision++;
        if (Current.Query == query && Current.Translation == translation) return;
        pages.RemoveRange(index + 1, pages.Count - index - 1);
        pages.Add(new(query, translation));
        if (pages.Count > Capacity) pages.RemoveAt(0);
        index = pages.Count - 1;
    }

    public QueryPage? Move(int direction)
    {
        if (direction != -1 && direction != 1 || direction == -1 && !CanBack || direction == 1 && !CanForward) return null;
        index += direction; Revision++;
        return Current;
    }
}
