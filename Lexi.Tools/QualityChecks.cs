using System.Text.Json;
using Lexi.Core;

internal static class QualityChecks
{
    public static void Check(string fixture)
    {
        var cases = JsonSerializer.Deserialize<QualityCase[]>(File.ReadAllText(fixture), Json.Flexible)!;
        if (cases.Length < 20 || cases.DistinctBy(c => c.Id).Count() != cases.Length) throw new Exception("Quality corpus incomplete");
        var item = cases.First(c => c.Id == "genre-cyber");
        var sense = new AiSense("noun", "a science fiction genre", "赛博朋克：科幻类型。", "文学", "She reads cyberpunk.", "她读赛博朋克小说。", [], Gloss: "赛博朋克", CoreMeaning: true, ChineseTerm: "赛博朋克");
        var entry = new AiEntry(item.Query, true, [sense], "", "unverified");
        if (DefinitionQuality.Evaluate(item, entry).Any(f => !f.Passed)) throw new Exception("Positive quality fixture rejected");
        var missingName = entry with { Senses = [sense with { Gloss = "高科技的科幻类型" }] };
        if (!DefinitionQuality.Evaluate(item, missingName).Any(f => f.Rule == "translation-cue" && !f.Passed)) throw new Exception("Missing translation escaped quality check");
        var narrow = entry with { Senses = [sense with { Register = "信息安全" }] };
        if (!DefinitionQuality.Evaluate(item, narrow).Any(f => f.Rule == "native-field" && !f.Passed)) throw new Exception("Wrong native field escaped quality check");
        if (!DefinitionQuality.Evaluate(item, entry with { EvidenceStatus = "source_based" }).Any(f => f.Rule == "evidence-consistency" && !f.Passed)) throw new Exception("Unsupported provenance escaped quality check");
        if (DefinitionQuality.Evaluate(item, entry with { Senses = [sense with { ChineseTerm = "赛博 朋克" }] }).Any(f => f.Rule == "translation-cue" && !f.Passed)) throw new Exception("Whitespace variant incorrectly failed quality check");
        var noCore = entry with { Senses = [sense with { CoreMeaning = false }] };
        if (!DefinitionQuality.Evaluate(item, noCore).Any(f => f.Rule == "centrality" && !f.Passed)) throw new Exception("Missing centrality escaped quality check");
        Console.WriteLine($"PASS: {cases.Length} quality cases loaded; translation, scope and centrality fault injection caught. No provider was called.");
    }
    // Replay externally collected complete provider answers. No network or paid model calls.
    public static void Score(string fixture, string answers)
    {
        var cases = JsonSerializer.Deserialize<QualityCase[]>(File.ReadAllText(fixture), Json.Flexible)!;
        var samples = JsonSerializer.Deserialize<QualitySample[]>(File.ReadAllText(answers), Json.Flexible)!;
        var report = cases.Select(c =>
        {
            var sample = samples.SingleOrDefault(s => s.Id == c.Id);
            var checks = sample == null ? DefinitionQuality.Evaluate(c, new(c.Query, false, [], "", "unknown")).Select(f => f with { Passed = false }).ToArray() : DefinitionQuality.Evaluate(c, sample.Entry);
            return new { c.Id, passed = checks.Count(f => f.Passed), total = checks.Length, failed = checks.Where(f => !f.Passed).Select(f => f.Rule).ToArray() };
        }).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new { scope = "heuristic lexical regression; factual accuracy needs human review", passed = report.Sum(c => c.passed), total = report.Sum(c => c.total), cases = report }, Json.Flexible));
    }
    private sealed record QualitySample(string Id, AiEntry Entry);
}
