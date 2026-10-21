using System.Text;
using System.Text.Json;

namespace Lexi.Core;

public static class StreamingEntry
{
    public static string Unwrap(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var newline = text.IndexOf('\n');
            if (newline < 0) return "";
            text = text[(newline + 1)..];
            if (text.TrimEnd().EndsWith("```", StringComparison.Ordinal)) text = text.TrimEnd()[..^3];
        }
        return text.Trim();
    }

    // Parse only complete JSON tokens. Incomplete strings, escapes and objects are never guessed.
    // Partial content is explicitly unverified and is never written to the cache.
    public static AiEntry? Read(string text, string query)
    {
        var bytes = Encoding.UTF8.GetBytes(Unwrap(text));
        var reader = new Utf8JsonReader(bytes, isFinalBlock: false, state: default);
        string? property = null, word = null, phonetic = null;
        bool inSenses = false;
        var senses = new List<AiSense>();
        Dictionary<string, string>? fields = null;
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName) property = reader.GetString();
                else if (reader.TokenType == JsonTokenType.String)
                {
                    if (reader.CurrentDepth == 1 && property == "word") word = reader.GetString();
                    if (reader.CurrentDepth == 1 && property == "phonetic") phonetic = reader.GetString();
                    if (fields != null && reader.CurrentDepth == 3 && property != null) fields[property] = reader.GetString() ?? "";
                }
                else if (reader.TokenType is JsonTokenType.True or JsonTokenType.False && fields != null && reader.CurrentDepth == 3 && property == "coreMeaning")
                    fields[property] = reader.TokenType == JsonTokenType.True ? "true" : "false";
                else if (reader.TokenType == JsonTokenType.StartArray && reader.CurrentDepth == 1 && property == "senses") inSenses = true;
                else if (reader.TokenType == JsonTokenType.StartObject && reader.CurrentDepth == 2 && inSenses) fields = new();
                else if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == 2 && fields != null)
                { AddSense(fields, senses, true); fields = null; }
                else if (reader.TokenType == JsonTokenType.EndArray && reader.CurrentDepth == 1) inSenses = false;
            }
        }
        catch (JsonException) { return null; }
        if (fields != null) AddSense(fields, senses, false);
        if (word == null || Queries.Normalize(word) != Queries.Normalize(query) || Queries.AbbreviationCandidate(query) && Queries.LookupKey(word) != query || senses.Count == 0 || senses.Count > 8) return null;
        return new(query, true, CodexDictionary.PrioritizeCurrent(senses), "", "unverified", Pronunciation.Format(phonetic));
    }

    // closed: the sense object has ended, so its gloss and core flag are final and may lead a
    // provisional TL;DR. A sense still being written never claims to be the core meaning.
    private static void AddSense(Dictionary<string, string> fields, List<AiSense> senses, bool closed)
    {
        string Get(string key) => fields.GetValueOrDefault(key, "");
        if (string.IsNullOrWhiteSpace(Get("chinese")) || string.IsNullOrWhiteSpace(Get("english"))) return;
        senses.Add(new(Get("partOfSpeech"), Get("english"), Get("chinese"), Get("register"), Get("example"), Get("exampleChinese"), [], Get("lexicalForm") == "abbreviation" ? "abbreviation" : "word", Get("expansion"),
            Gloss: closed ? QuickRead.ValidGloss(Get("gloss")) : "", CoreMeaning: closed && fields.TryGetValue("coreMeaning", out var core) ? core == "true" : null,
            ChineseTerm: QuickRead.ValidGloss(Get("chineseTerm"))));
    }
}
