using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lexi.Core;

public sealed record PronunciationEntry(string Word, string[] Ipa, string SourceUrl, DateTimeOffset FetchedAt);

public sealed partial class Pronunciation : IDisposable
{
    private readonly HttpClient client;
    public Pronunciation(HttpMessageHandler? handler = null)
    {
        client = handler == null ? new() : new(handler);
        client.Timeout = TimeSpan.FromSeconds(5);
        client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LexiDictionary/0.6 (Windows dictionary)");
    }
    public async Task<PronunciationEntry?> LookupAsync(string word, CancellationToken token)
    {
        if (Queries.HasChinese(word)) return null;
        var url = "https://en.wiktionary.org/w/api.php?action=parse&prop=wikitext&format=json&formatversion=2&redirects=1&page=" + Uri.EscapeDataString(word);
        var json = await client.GetStringAsync(url, token);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("parse", out var parsed) || !parsed.TryGetProperty("wikitext", out var text)) return null;
        var values = ParseEnglish(text.GetString() ?? "");
        return values.Length == 0 ? null : new(word, values, "https://en.wiktionary.org/wiki/" + Uri.EscapeDataString(word) + "#English", DateTimeOffset.UtcNow);
    }
    public static string[] ParseEnglish(string wikitext)
    {
        var english = English().Match(wikitext).Groups[1].Value;
        return Ipa().Matches(english).SelectMany(m => m.Groups[1].Value.Split('|')).Select(s => s.Trim())
            .Where(s => !s.Contains('=') && Valid(s) && (s.StartsWith('/') && s.EndsWith('/') || s.StartsWith('[') && s.EndsWith(']')))
            .Distinct().Take(4).ToArray();
    }
    public static bool Valid(string? value) => value != null && value.Length <= 160 && !value.Any(char.IsControl) && !value.Contains('<') && !value.Contains('{');
    [GeneratedRegex(@"^==[ \t]*English[ \t]*==[ \t]*\r?\n(.*?)(?=^==[^=]|\z)", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex English();
    [GeneratedRegex(@"\{\{IPA\|en\|([^{}]*)\}\}", RegexOptions.IgnoreCase)] private static partial Regex Ipa();
    public void Dispose() => client.Dispose();
}
