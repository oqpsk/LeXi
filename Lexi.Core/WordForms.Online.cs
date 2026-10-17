using System.Text.RegularExpressions;

namespace Lexi.Core;

public static partial class WordForms
{
    // Read the expanded headword line: Wiktionary has already applied its irregular
    // forms and spelling rules. We do not infer endings from the search string.
    public static WordFormSet[] ParseEnglishHtml(string html, string sourceUrl)
    {
        var entries = new List<WordFormSet>();
        foreach (Match paragraph in Paragraphs().Matches(html))
        {
            var content = paragraph.Groups[1].Value;
            if (!content.Contains("class=\"headword-line\"", StringComparison.Ordinal)) continue;
            var head = Headword().Match(content);
            if (!head.Success) continue;
            var lemma = OnlineDictionary.Plain(head.Groups[1].Value);
            if (!ValidForm(lemma)) continue;
            var forms = new List<WordForm>();
            string[] previousKinds = [];
            foreach (Match section in FormSections().Matches(content))
            {
                var label = OnlineDictionary.Plain(section.Groups[1].Value).ToLowerInvariant();
                string[] kinds = label switch
                {
                    "or" => previousKinds,
                    "third-person singular simple present" => ["3"],
                    "present participle" => ["i"],
                    "simple past" => ["p"],
                    "past participle" => ["d"],
                    "simple past and past participle" => ["p", "d"],
                    "plural" => ["s"], "comparative" => ["r"], "superlative" => ["t"],
                    _ => []
                };
                previousKinds = kinds;
                var words = FormValues().Matches(section.Groups[2].Value).Select(m => OnlineDictionary.Plain(m.Groups[1].Value))
                    .Where(ValidForm).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
                if (words.Length == 0) continue;
                foreach (var kind in kinds) forms.Add(new(kind, Types.First(t => t.Key == kind).Label, words));
            }
            if (forms.Count > 0) entries.Add(new(lemma, forms.ToArray(), "Wiktionary · CC BY-SA 4.0", sourceUrl));
        }
        return Combine(entries.ToArray());
    }
    private static bool ValidForm(string word) => word.Length is > 0 and <= 100 &&
        word.Any(char.IsLetter) && word.All(c => char.IsLetter(c) || c is ' ' or '-' or '\'' or '’');
    [GeneratedRegex(@"<p\b[^>]*>(.*?)</p>", RegexOptions.Singleline)] private static partial Regex Paragraphs();
    [GeneratedRegex("<strong\\b(?=[^>]*class=\"[^\"]*\\bheadword\\b)(?=[^>]*lang=\"en\")[^>]*>(.*?)</strong>", RegexOptions.Singleline)]
    private static partial Regex Headword();
    [GeneratedRegex(@"<i>(.*?)</i>(.*?)(?=<i>|\z)", RegexOptions.Singleline)] private static partial Regex FormSections();
    [GeneratedRegex("<b\\b(?=[^>]*class=\"[^\"]*\\bform-of\\b)(?=[^>]*lang=\"en\")[^>]*>(.*?)</b>", RegexOptions.Singleline)]
    private static partial Regex FormValues();
}
