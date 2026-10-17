using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lexi.Core;

public sealed record EtymologyExcerpt(string Label, string Text, string SourceUrl);
public sealed record PronunciationEntry(string Word, string[] Ipa, string SourceUrl, DateTimeOffset FetchedAt,
    WordFormSet[]? Forms = null, EtymologyExcerpt[]? Etymologies = null);

public sealed partial class Pronunciation : IDisposable
{
    private readonly HttpClient client;
    public Pronunciation(HttpMessageHandler? handler = null)
    {
        client = handler == null ? new() : new(handler);
        client.Timeout = TimeSpan.FromSeconds(5);
        client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LeXiDictionary/0.9 (Windows dictionary)");
    }
    public async Task<PronunciationEntry?> LookupAsync(string word, CancellationToken token)
    {
        if (Queries.HasChinese(word)) return null;
        var url = "https://en.wiktionary.org/w/api.php?action=parse&prop=wikitext%7Ctext&format=json&formatversion=2&redirects=1&page=" + Uri.EscapeDataString(word);
        var json = await client.GetStringAsync(url, token);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("parse", out var parsed) || parsed.ValueKind != JsonValueKind.Object)
        {
            var lower = Queries.Normalize(word);
            if (word != lower && !Queries.AbbreviationCandidate(word))
            {
                var fallback = await LookupAsync(lower, token);
                return fallback == null ? null : fallback with { Word = word };
            }
            return null;
        }
        if (Queries.AbbreviationCandidate(word) && parsed.TryGetProperty("title", out var title) &&
            title.ValueKind == JsonValueKind.String && title.GetString() != word) return null;
        var sourceUrl = "https://en.wiktionary.org/wiki/" + Uri.EscapeDataString(word) + "#English";
        var markup = parsed.TryGetProperty("wikitext", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() ?? "" : "";
        var html = parsed.TryGetProperty("text", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() ?? "" : "";
        var values = ParseEnglishHtml(html).Concat(ParseEnglish(markup)).Distinct().Take(4).ToArray();
        var forms = WordForms.ParseEnglishHtml(html, sourceUrl);
        // An acronym page cannot inherit a redirected lower-case homograph's forms.
        if (Queries.AbbreviationCandidate(word)) forms = forms.Where(f => f.Lemma == word).ToArray();
        var etymologies = ParseEtymologies(html, sourceUrl);
        return values.Length == 0 && forms.Length == 0 && etymologies.Length == 0 ? null : new(word, values, sourceUrl, DateTimeOffset.UtcNow, forms, etymologies);
    }

    public static string Format(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 160 || raw.Any(char.IsControl) || raw.Contains('\\')) return "";
        var text = raw.Trim();
        // ECDICT uses a straight stress apostrophe and colon for vowel length.
        if (text.Length > 1 && (text[0] == '/' && text[^1] == '/' || text[0] == '[' && text[^1] == ']')) text = text[1..^1].Trim();
        text = text.Replace("'", "ˈ").Replace(":", "ː");
        if (text.Length == 0 || !text.Any(char.IsLetter) || !text.All(c =>
            char.IsLetter(c) && (c <= 0x02ff || c is >= '\u1d00' and <= '\u1dbf') ||
            CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark || " ˈˌːˑ.‿͜͡()".Contains(c))) return "";
        return "/" + text + "/";
    }
    public static bool Valid(string? value) => Format(value).Length > 0;
    public static string[] ParseEnglish(string wikitext)
    {
        var english = English().Match(wikitext).Groups[1].Value;
        return Ipa().Matches(english).SelectMany(m => m.Groups[1].Value.Split('|')).Select(s => s.Trim())
            .Where(s => !s.Contains('=') && (s.StartsWith('/') && s.EndsWith('/') || s.StartsWith('[') && s.EndsWith(']')))
            .Select(Format).Where(s => s.Length > 0).Distinct().Take(4).ToArray();
    }
    private static string EnglishHtml(string html)
    {
        var heading = EnglishHeading().Matches(html).FirstOrDefault(m => OnlineDictionary.Plain(m.Value).Replace("[edit]", "").Trim() == "English");
        if (heading == null) return "";
        var rest = html[(heading.Index + heading.Length)..];
        var next = NextLanguage().Match(rest);
        return next.Success ? rest[..next.Index] : rest;
    }
    public static string[] ParseEnglishHtml(string html) => HtmlIpa().Matches(EnglishHtml(html))
        .Select(m => Format(OnlineDictionary.Plain(m.Groups[1].Value))).Where(s => s.Length > 0).Distinct().Take(4).ToArray();
    public static EtymologyExcerpt[] ParseEtymologies(string html, string sourceUrl)
    {
        var english = EnglishHtml(html);
        var result = new List<EtymologyExcerpt>();
        foreach (Match header in EtymologyHeading().Matches(english))
        {
            if (!OnlineDictionary.Plain(header.Groups[1].Value).Trim().StartsWith("Etymology", StringComparison.Ordinal)) continue;
            var rest = english[(header.Index + header.Length)..];
            var next = NextSection().Match(rest);
            var section = next.Success ? rest[..next.Index] : rest;
            var paragraphs = Paragraphs().Matches(section).Select(m => OnlineDictionary.Plain(Superscripts().Replace(m.Groups[1].Value, "")))
                .Where(s => s.Length > 0).Take(3).ToArray();
            var value = string.Join("\n\n", paragraphs);
            if (value.Length == 0) continue;
            if (value.Length > 1800) value = value[..1800] + "…";
            result.Add(new(OnlineDictionary.Plain(header.Groups[1].Value).Replace("[edit]", "").Trim(), value, sourceUrl));
        }
        return result.DistinctBy(e => e.Text).Take(6).ToArray();
    }
    [GeneratedRegex(@"^==[ \t]*English[ \t]*==[ \t]*\r?\n(.*?)(?=^==[^=]|\z)", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex English();
    [GeneratedRegex(@"\{\{IPA\|en\|([^{}]*)\}\}", RegexOptions.IgnoreCase)] private static partial Regex Ipa();
    [GeneratedRegex(@"<h2\b[^>]*>.*?</h2>", RegexOptions.Singleline)] private static partial Regex EnglishHeading();
    [GeneratedRegex(@"<h2\b", RegexOptions.Singleline)] private static partial Regex NextLanguage();
    [GeneratedRegex("<span\\b[^>]*class=\"(?:[^\"]* )?IPA(?: [^\"]*)?\"[^>]*>(.*?)</span>", RegexOptions.Singleline)] private static partial Regex HtmlIpa();
    [GeneratedRegex(@"<h[34]\b[^>]*>(.*?)</h[34]>", RegexOptions.Singleline)] private static partial Regex EtymologyHeading();
    [GeneratedRegex(@"<h[2-6]\b", RegexOptions.Singleline)] private static partial Regex NextSection();
    [GeneratedRegex(@"<p\b[^>]*>(.*?)</p>", RegexOptions.Singleline)] private static partial Regex Paragraphs();
    [GeneratedRegex(@"<sup\b[^>]*>.*?</sup>", RegexOptions.Singleline)] private static partial Regex Superscripts();
    public void Dispose() => client.Dispose();
}
