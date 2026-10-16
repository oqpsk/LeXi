using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lexi.Core;

public sealed partial class OnlineDictionary : IDisposable
{
    private readonly HttpClient client;
    public OnlineDictionary(HttpMessageHandler? handler = null)
    {
        client = handler == null ? new HttpClient() : new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(12);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LexiDictionary/0.1 (Windows personal dictionary; Wiktionary reader)");
    }
    public async Task<OnlineEntry?> LookupAsync(string query, CancellationToken token)
    {
        var url = "https://en.wiktionary.org/api/rest_v1/page/definition/" + Uri.EscapeDataString(query);
        using var response = await client.GetAsync(url, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(token);
        return Parse(query, text);
    }
    public static OnlineEntry? Parse(string query, string json)
    {
        using var document = JsonDocument.Parse(json);
        var chinese = Queries.HasChinese(query);
        if (!document.RootElement.TryGetProperty(chinese ? "zh" : "en", out var groups)) return null;
        var senses = new List<Sense>();
        foreach (var group in groups.EnumerateArray())
        {
            // The en bucket can contain Translingual entries; filter the explicit language.
            var language = group.GetProperty("language").GetString();
            if (chinese ? language is not "Chinese" and not "Mandarin" : language != "English") continue;
            var pos = group.GetProperty("partOfSpeech").GetString() ?? "";
            foreach (var entry in group.GetProperty("definitions").EnumerateArray())
            {
                var definition = Plain(entry.GetProperty("definition").GetString() ?? "");
                if (definition.Length == 0) continue;
                var examples = entry.TryGetProperty("examples", out var ex) && ex.ValueKind == JsonValueKind.Array
                    ? ex.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => Plain(x.GetString()!)).Take(2).ToArray() : [];
                senses.Add(new(pos, definition, examples));
            }
        }
        return senses.Count == 0 ? null : new(query, senses.Take(40).ToArray(),
            "https://en.wiktionary.org/wiki/" + Uri.EscapeDataString(query) + (chinese ? "#Chinese" : "#English"), DateTimeOffset.UtcNow);
    }
    public static string Plain(string html) => Spaces().Replace(WebUtility.HtmlDecode(Tags().Replace(Script().Replace(html, ""), "")), " ").Trim();
    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)] private static partial Regex Script();
    [GeneratedRegex(@"<[^>]*>")] private static partial Regex Tags();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    public void Dispose() => client.Dispose();
}
