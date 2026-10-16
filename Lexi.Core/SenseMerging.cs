using System.Text.Json;

namespace Lexi.Core;

public sealed record SenseCandidate(string Id, LookupModel Model, AiSense Sense, string EvidenceStatus, bool Provisional);
public sealed record SenseGroup(SenseCandidate[] Variants)
{
    // Stable model order preserves the user's primary wording. Citations are
    // provenance, not a quality score; agreement never upgrades either source.
    public SenseCandidate Representative => Variants[0];
}
public sealed record SensePair(string A, string B);
public sealed record SenseVerdict(string A, string B, string Relation, double Confidence);
public sealed record SenseMergePlan(string InputHash, SenseVerdict[] Pairs);
public sealed record SenseMergeRequest(string Query, SenseCandidate[] Candidates, SensePair[] Pairs)
{
    public string InputHash => Cache.Hash(JsonSerializer.Serialize(new { version = 1, Query, Candidates, Pairs }, Json.Options));
    public string Prompt => JsonSerializer.Serialize(new
    {
        task = "compare_senses", inputHash = InputHash, query = Query,
        senses = Candidates.Select(c => new { c.Id, c.Sense.PartOfSpeech, c.Sense.English, c.Sense.Chinese, c.Sense.Register, c.Sense.Example }),
        pairs = Pairs
    }, Json.Options);
}

public static class SenseMerging
{
    public static SenseCandidate[] Candidates(ModelAnswer[] answers) => answers.SelectMany((answer, modelIndex) =>
        (answer.Entry?.Senses ?? []).Select((sense, senseIndex) => new SenseCandidate($"m{modelIndex}s{senseIndex}",
            answer.Model, sense, answer.Entry!.EvidenceStatus, answer.Phase == ModelPhase.Streaming))).ToArray();

    public static bool Exact(SenseCandidate a, SenseCandidate b) =>
        Queries.Normalize(a.Sense.English) == Queries.Normalize(b.Sense.English) &&
        Queries.Normalize(a.Sense.Chinese) == Queries.Normalize(b.Sense.Chinese) &&
        Queries.Normalize(a.Sense.PartOfSpeech) == Queries.Normalize(b.Sense.PartOfSpeech) &&
        Queries.Normalize(a.Sense.Register) == Queries.Normalize(b.Sense.Register);

    public static SenseMergeRequest Request(string query, ModelAnswer[] answers)
    {
        var candidates = Candidates(answers).Where(c => !c.Provisional).ToArray();
        var pairs = new List<SensePair>();
        for (int i = 0; i < candidates.Length; i++)
            for (int j = i + 1; j < candidates.Length; j++)
                if (candidates[i].Model != candidates[j].Model && !Exact(candidates[i], candidates[j]))
                    pairs.Add(new(candidates[i].Id, candidates[j].Id));
        return new(query, candidates, pairs.ToArray());
    }

    public static SenseMergePlan Parse(string json, SenseMergeRequest request)
    {
        var plan = JsonSerializer.Deserialize<SenseMergePlan>(StreamingEntry.Unwrap(json), Json.Options);
        if (plan == null || plan.InputHash != request.InputHash || plan.Pairs == null || plan.Pairs.Length != request.Pairs.Length)
            throw new InvalidDataException("义项判断结果不完整或与当前查询不匹配。");
        var expected = request.Pairs.Select(p => PairKey(p.A, p.B)).ToHashSet();
        foreach (var pair in plan.Pairs)
            if (pair == null || !expected.Remove(PairKey(pair.A, pair.B)) ||
                pair.Relation is not ("equivalent" or "different" or "uncertain") ||
                !double.IsFinite(pair.Confidence) || pair.Confidence < 0 || pair.Confidence > 1)
                throw new InvalidDataException("义项判断包含无效或重复的比较结果。");
        return plan;
    }

    public static SenseGroup[] Group(ModelAnswer[] answers, SenseMergePlan? plan = null)
    {
        var candidates = Candidates(answers);
        var approved = (plan?.Pairs ?? []).Where(p => p.Relation == "equivalent" && p.Confidence >= 0.95)
            .Select(p => PairKey(p.A, p.B)).ToHashSet();
        bool Same(SenseCandidate a, SenseCandidate b) => Exact(a, b) ||
            !a.Provisional && !b.Provisional && approved.Contains(PairKey(a.Id, b.Id));
        var groups = new List<List<SenseCandidate>>();
        foreach (var candidate in candidates)
        {
            // Complete-link grouping avoids A=B, B=C incorrectly hiding A!=C.
            var group = groups.FirstOrDefault(g => g.All(existing => Same(existing, candidate)));
            if (group == null) groups.Add([candidate]); else group.Add(candidate);
        }
        return groups.Select(g => new SenseGroup(g.ToArray())).OrderBy(g => g.Variants.Any(v => IsCurrent(v.Sense.Register)) ? 0 : 1).ToArray();
    }
    private static string PairKey(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + ":" + b : b + ":" + a;
    public static bool IsCurrent(string? label) => new[] { "slang", "internet", "网络", "俚语" }
        .Any(word => (label ?? "").Contains(word, StringComparison.OrdinalIgnoreCase));
}
