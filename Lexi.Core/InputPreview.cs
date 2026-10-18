namespace Lexi.Core;

public sealed record InputPreviewView(string Query, string Headword, string Text, string Source);

// Read-only input assistance. It never starts providers, records history, changes a QueryView,
// or promotes an index's case-insensitive match to a case-sensitive saved abbreviation.
public static class InputPreview
{
    public static async Task<InputPreviewView?> ReadAsync(string text, SavedQueries saved, Task<OfflineLibrary> library,
        AppSettings settings, CancellationToken token = default)
    {
        if (Queries.Validate(text) != null || text.Contains('\n') || text.Length > 100) return null;
        var query = Queries.LookupKey(text);
        return await Task.Run(async () =>
        {
            token.ThrowIfCancellationRequested();
            if (saved.Dictionary(query) is { } cached)
            {
                var display = new QueryPresentation();
                display.BeginDictionary(query, query); display.BeginOverview(cached.Local);
                display.ApplyAnswers(cached.Result, cached.Evidence);
                if (display.QuickRead is { Visible: true, Text.Length: > 0 } quick)
                    return new InputPreviewView(query, query, quick.Text, "已保存结果 · " + quick.State);
            }
            if (!settings.OfflineEnabled) return null;
            var local = (await library.WaitAsync(token)).Lookup(query, settings);
            token.ThrowIfCancellationRequested();
            // Lookup performs exact/indexed-inflection lookup, never prefix completion.
            foreach (var hit in local)
            {
                var value = new[] { hit.Entry.Chinese, hit.Entry.English, hit.Entry.Original }
                    .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
                if (value == null) continue;
                var excerpt = QuickRead.Excerpt(value);
                if (excerpt.Length == 0) excerpt = value.Split('\n').First(t => !string.IsNullOrWhiteSpace(t)).Trim();
                // Keep complete source text. The native card limits its visible lines and offers full text in its hint.
                return new InputPreviewView(query, hit.Entry.Word, excerpt, hit.Dictionary.Name + " · 词典摘录");
            }
            return null;
        }, token);
    }
}
