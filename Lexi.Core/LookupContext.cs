using System.Text.Json;

namespace Lexi.Core;

// The sentence a word was selected in, and which of the entry's senses it means there. The entry
// itself stays context-free and cached as before; only this small pick depends on the sentence.
public sealed record ContextSense(string Id, string PartOfSpeech, string Chinese, string English);
public sealed record ContextPick(string InputHash, string SenseId, string Gloss);
// Named shapes rather than anonymous objects, so the JSON can also be generated ahead of time (iOS).
public sealed record ContextIdentity(int Version, string Query, string Sentence, ContextSense[] Senses);
public sealed record ContextPrompt(string Task, string InputHash, string Query, string Sentence, ContextSense[] Senses);
public sealed record ContextRequest(string Query, string Sentence, ContextSense[] Senses)
{
    public string InputHash => Cache.Hash(JsonSerializer.Serialize(new ContextIdentity(2, Query, Sentence, Senses), Json.Options));
    public string Prompt => JsonSerializer.Serialize(new ContextPrompt("pick_context_sense", InputHash, Query, Sentence, Senses), Json.Options);
}

public static class LookupContext
{
    public const string CacheKind = "context-picks-v1";
    public const int SentenceLimit = 400;
    public const int GlossLimit = 40;
    private static readonly char[] Ends = ['.', '!', '?', '。', '！', '？', '\n', '\r'];

    // The sentence around [start, start + length) in text that came from another app, or null when
    // there is nothing beyond the selection itself. Offsets are UTF-16, as in the front ends' APIs.
    public static string? Sentence(string? text, int start, int length)
    {
        if (string.IsNullOrEmpty(text) || start < 0 || length <= 0 || start + length > text.Length) return null;
        // The search starts outside the selection, so a full stop inside it ("U.S.") ends nothing.
        var from = start == 0 ? 0 : text.LastIndexOfAny(Ends, start - 1) + 1;
        var end = text.IndexOfAny(Ends, start + length);
        var to = end < 0 ? text.Length : end + 1;
        if (to - from > SentenceLimit)
        {
            from = Math.Max(from, start - (SentenceLimit - length) / 2);
            to = Math.Min(to, from + SentenceLimit);
        }
        var sentence = string.Join(" ", text[from..to].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var selected = text.Substring(start, length).Trim();
        return sentence.Length > selected.Length && Queries.Validate(sentence) == null ? sentence : null;
    }

    // Every completed sense, in model and answer order, whatever the merge later groups together:
    // the same answers always make the same request, so a pick made while the merge runs is the
    // one found again when the page is restored. Ids are the senses' view keys.
    public static ContextRequest? Request(string query, string? sentence, LookupSnapshot? snapshot)
    {
        if (string.IsNullOrWhiteSpace(sentence) || snapshot == null) return null;
        var senses = SenseMerging.Candidates(snapshot.Answers).Where(c => !c.Provisional)
            .Select(c => new ContextSense(QueryPresentation.SenseKey(c), LexicalLabels.PartOfSpeech(c.Sense.PartOfSpeech),
                QuickRead.ChineseDefinition(c.Sense), c.Sense.English)).DistinctBy(s => s.Id).ToArray();
        return senses.Length == 0 ? null : new(query, sentence, senses);
    }

    public static ContextPick Parse(string json, ContextRequest request)
    {
        var pick = JsonSerializer.Deserialize<ContextPick>(StreamingEntry.Unwrap(json), Json.Options);
        if (pick == null || pick.InputHash != request.InputHash || pick.SenseId == null || pick.Gloss == null)
            throw new InvalidDataException("语境判断结果不完整或与当前查询不匹配。");
        var gloss = string.Join(" ", pick.Gloss.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (pick.SenseId.Length > 0 && request.Senses.All(s => s.Id != pick.SenseId) || gloss.Length > GlossLimit ||
            pick.SenseId.Length > 0 && gloss.Length == 0)
            throw new InvalidDataException("语境判断包含无效的义项或说明。");
        return pick with { Gloss = gloss };
    }
}
