namespace Lexi.Core;

public static class ModelInflections
{
    private static readonly Dictionary<string, (string Key, string Label, string Pos)> Kinds = new()
    {
        ["past"] = ("p", "过去式", "verb"), ["pastParticiple"] = ("d", "过去分词", "verb"),
        ["presentParticiple"] = ("i", "现在分词", "verb"), ["thirdPerson"] = ("3", "第三人称单数", "verb"),
        ["plural"] = ("s", "复数", "noun"), ["comparative"] = ("r", "比较级", "adjective"),
        ["superlative"] = ("t", "最高级", "adjective")
    };
    private static bool Spelling(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 100 &&
        !Queries.HasChinese(text) && text.Any(char.IsLetter) && text.All(c => char.IsLetter(c) || c is ' ' or '-' or '\'' or '’');

    // Morphology is optional enrichment. Invalid groups never destroy usable definitions.
    public static AiWordForms[]? Sanitize(string query, AiEntry entry)
    {
        if (entry.WordForms == null) return null; // Legacy cache: enrichment has not run yet.
        if (!entry.Found || Queries.HasChinese(query)) return [];
        var classes = entry.Senses.Select(s => LexicalLabels.PartOfSpeech(s.PartOfSpeech)).ToHashSet();
        var valid = new List<AiWordForms>();
        foreach (var group in entry.WordForms.Take(8))
        {
            if (group == null || !Spelling(group.Lemma) || group.PartOfSpeech == null || group.Forms == null) continue;
            var pos = LexicalLabels.PartOfSpeech(group.PartOfSpeech);
            if (!classes.Contains(pos) || pos is not ("verb" or "noun" or "adjective" or "adverb")) continue;
            var forms = group.Forms.Take(16).Where(f => f != null && f.Kind != null && f.Words != null &&
                Kinds.TryGetValue(f.Kind, out var kind) && (kind.Pos == pos || kind.Pos == "adjective" && pos == "adverb"))
                .GroupBy(f => f.Kind).Select(g => new AiInflection(g.Key, g.SelectMany(f => f.Words).Where(Spelling)
                    .Select(w => w.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToArray())).Where(f => f.Words.Length > 0).ToArray();
            var lemma = group.Lemma.Trim();
            if (forms.Length == 0 || Queries.Normalize(lemma) != Queries.Normalize(query) &&
                !forms.SelectMany(f => f.Words).Any(w => Queries.Normalize(w) == Queries.Normalize(query))) continue;
            valid.Add(new(lemma, pos, forms));
        }
        return valid.ToArray();
    }

    public static WordFormSet[] Supplement(string query, WordFormSet[] dictionary, IEnumerable<(LookupModel Model, AiEntry Entry)> answers)
    {
        var result = WordForms.Combine(dictionary).ToList();
        foreach (var (model, entry) in answers)
        foreach (var group in Sanitize(query, entry) ?? [])
        {
            var index = result.FindIndex(s => Queries.Normalize(s.Lemma) == Queries.Normalize(group.Lemma));
            var set = index < 0 ? new WordFormSet(group.Lemma, [], model.Name + " · 模型补充", "") : result[index];
            var forms = set.Forms.ToList();
            foreach (var supplied in group.Forms)
            {
                var kind = Kinds[supplied.Kind];
                var existing = forms.FindIndex(f => f.Kind == kind.Key);
                if (existing < 0)
                    forms.Add(new(kind.Key, kind.Label, supplied.Words, model.Name + " · 模型补充", "", "模型随释义提供；当前词典未记录这一项变化。"));
                else
                {
                    var different = supplied.Words.Except(forms[existing].Words, StringComparer.OrdinalIgnoreCase).ToArray();
                    if (different.Length == 0) continue;
                    // Keep the dictionary or configured primary spelling visible, with other
                    // proposals available for inspection. Agreement is not source verification.
                    var note = model.Name + " 另给出：" + string.Join(" / ", different) + "（模型补充）";
                    forms[existing] = forms[existing] with { Note = string.Join("\n", new[] { forms[existing].Note, note }.Where(s => s.Length > 0).Distinct()) };
                }
            }
            set = set with { Forms = forms.ToArray() };
            if (index < 0) result.Add(set); else result[index] = set;
        }
        return result.ToArray();
    }
}
