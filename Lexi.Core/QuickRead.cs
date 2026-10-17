namespace Lexi.Core;

public sealed record QuickMeaning(string Text, string Label);

// Reuse completed definitions. Never synthesize a new claim or cut a definition
// mid-clause to satisfy the visual budget. Older caches work without glosses.
public static class QuickRead
{
    public static string ValidGloss(string? value) => value is { Length: > 0 and <= 32 } &&
        Queries.HasChinese(value) && !value.Any(char.IsControl) ? value.Trim() : "";
    public static string Excerpt(string value)
    {
        value = value.Trim();
        if (value.Length <= 60) return value.TrimEnd('。', '.', '；', ';');
        var sentence = value.Split(['。', '\n'], 2)[0].Trim();
        return sentence.Length <= 60 ? sentence : "";
    }
    public static QuickMeaning[] Create(SenseGroup[] groups, SpecialistReference[] uncovered, LocalHit[] local)
    {
        var choices = new List<QuickMeaning>();
        foreach (var brief in uncovered)
            choices.Add(new(Excerpt(brief.Chinese), LexicalLabels.Usage(brief.Domain)));
        foreach (var group in groups)
        {
            var candidate = group.Variants.FirstOrDefault(v => !v.Provisional);
            if (candidate == null) continue;
            var sense = candidate.Sense;
            var text = ValidGloss(sense.Gloss);
            if (text.Length == 0) text = Excerpt(sense.Chinese);
            var label = LexicalLabels.Usage(sense.Register);
            choices.Add(new(text, label.Length > 0 ? label : LexicalLabels.PartOfSpeech(sense.PartOfSpeech)));
        }
        if (choices.All(c => c.Text.Length == 0))
            choices.AddRange(local.Where(h => h.Dictionary.AllowAiEvidence).Select(h =>
                new QuickMeaning(Excerpt(h.Entry.Chinese), h.Dictionary.Name)));
        return choices.Where(c => c.Text.Length > 0).DistinctBy(c => c.Text).Take(2).ToArray();
    }
}
