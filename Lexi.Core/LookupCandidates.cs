using System.Text.RegularExpressions;

namespace Lexi.Core;

// Text a front end found when the reader summoned LeXi from another app: the selection there, or
// what they copied a moment earlier. Words and short phrases are looked up at once. Longer text only
// fills the query field, because translating a copied paragraph costs a model call the reader may
// not have meant to make. Links, paths and e-mail addresses are not lookups.
// Context is the sentence a selected word or phrase came from, when the front end could read it.
public sealed record LookupCandidate(string Text, bool Submit, string? Context = null);

public static partial class LookupCandidates
{
    public const int SubmitLength = 64;
    // Quotes and punctuation a copied word often brings along; a final full stop is handled apart,
    // because it belongs to abbreviations such as "U.S." and marks a sentence for translation.
    private static readonly char[] Edge = ",;:!?\"'“”‘’()[]{}<>«»「」『』《》（）【】，；：！？、".ToCharArray();

    // surrounding and start: text around the selection and where the selection begins in it (UTF-16).
    public static LookupCandidate? From(string? raw, string? surrounding = null, int start = -1)
    {
        var text = (raw ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (Queries.Validate(text) != null || NotALookup().IsMatch(text)) return null;
        var single = !text.Contains('\n');
        if (single)
        {
            text = text.Trim(Edge).Trim();
            if (!text.Contains(' ') && text.EndsWith('.') && text.IndexOf('.') == text.Length - 1) text = text[..^1];
            if (text.Length == 0) return null;
        }
        var submit = single && text.Length <= SubmitLength;
        var context = submit && surrounding != null && raw != null ? LookupContext.Sentence(surrounding, start, raw.Length) : null;
        return new(text, submit, context);
    }

    [GeneratedRegex(@"^(?:[a-z][a-z0-9+.-]*://\S*|www\.\S+|mailto:\S+|[\w.+-]+@[\w-]+\.[\w.-]+|~?/\S*|[A-Za-z]:\\\S*)$", RegexOptions.IgnoreCase)]
    private static partial Regex NotALookup();
}
