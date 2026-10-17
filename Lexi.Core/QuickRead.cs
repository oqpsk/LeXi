namespace Lexi.Core;

public sealed record QuickMeaning(string Text, string Label, bool Core = false);

// Select existing completed definitions. Domain/display order is not evidence
// of centrality. Legacy/offline excerpts remain available but are labeled as such.
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
        var complete = groups.Where(g => g.Variants.Any(v => !v.Provisional)).ToArray();
        var assessed = complete.Any(g => g.OverviewAssessed);
        var selected = assessed
            ? complete.Where(g => g.OverviewRank != null).OrderBy(g => g.OverviewRank).ToArray()
            : complete.Where(g => g.Variants.Any(v => !v.Provisional && v.Sense.CoreMeaning == true)).ToArray();
        if (!assessed && selected.Length == 0 && complete.Length == 1 && uncovered.Length == 0 &&
            complete[0].Variants.All(v => v.Sense.CoreMeaning == null)) selected = complete;
        var core = selected.Length > 0;
        var choices = new List<QuickMeaning>();
        foreach (var group in core ? selected : complete)
        {
            var candidate = group.Variants.First(v => !v.Provisional);
            var sense = candidate.Sense;
            var text = ValidGloss(sense.Gloss);
            if (text.Length == 0) text = Excerpt(sense.Chinese);
            var label = LexicalLabels.Usage(sense.Register);
            choices.Add(new(text, label.Length > 0 ? label : LexicalLabels.PartOfSpeech(sense.PartOfSpeech), core));
        }
        // Do not pad a deliberately short core summary with unrelated meanings.
        if (!core)
            choices.InsertRange(0, uncovered.Select(b => new QuickMeaning(Excerpt(b.Chinese), LexicalLabels.Usage(b.Domain))));
        if (choices.All(c => c.Text.Length == 0) && !core)
            choices.AddRange(local.Where(h => h.Dictionary.AllowAiEvidence).Select(h =>
                new QuickMeaning(Excerpt(h.Entry.Chinese), h.Dictionary.Name)));
        return choices.Where(c => c.Text.Length > 0).DistinctBy(c => c.Text).Take(2).ToArray();
    }
}
