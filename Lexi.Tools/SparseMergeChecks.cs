using System.Net;
using System.Text;
using System.Text.Json;
using Lexi.Core;

internal static class SparseMergeChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var model = new LookupModel("codex", "synthetic-judge");
        AiSense Sense(int i) => new("noun", $"Synthetic concept {i / 2}, wording {i % 2}.", $"合成概念 {i / 2}，表述 {i % 2}。", "", "", "", []);
        var entry = new AiEntry("synthetic", true, Enumerable.Range(0, 20).Select(Sense).ToArray(), "", "unverified");
        var answers = new[] { new ModelAnswer(model, ModelPhase.Complete, entry) };
        var request = SenseMerging.Request("synthetic", answers);
        var positives = Enumerable.Range(0, 10).Select(i => new SenseVerdict($"m0s{i * 2}", $"m0s{i * 2 + 1}", "equivalent", 0.99)).ToArray();
        var compact = new SenseMergePlan(request.InputHash, positives, ["m0s0"], request.Pairs.Length);
        var full = new SenseMergePlan(request.InputHash, request.Pairs.Select(p => positives.FirstOrDefault(v => v.A == p.A && v.B == p.B) ?? new(p.A, p.B, "different", 0.99)).ToArray(), ["m0s0"]);
        string Encode(SenseMergePlan plan) => JsonSerializer.Serialize(plan, Json.Options);
        var parsed = SenseMerging.Parse(Encode(compact), request);
        string[][] Groups(SenseMergePlan plan) => SenseMerging.Group(answers, plan).Select(g => g.Variants.Select(v => v.Id).ToArray()).ToArray();
        check(request.Pairs.Length == 190 && JsonSerializer.Serialize(Groups(parsed)) == JsonSerializer.Serialize(Groups(full)) && parsed.Pairs.Length == 190,
            "sparse positive judgments preserve the fully enumerated grouping across all 190 comparisons");
        check(parsed.Pairs.Count(p => p.Relation == "uncertain" && p.Confidence == 0) == 180 && parsed.Overview!.SequenceEqual(["m0s0"]),
            "omitted comparisons remain uncertain while overview selection is preserved");
        var sparseBytes = Encoding.UTF8.GetByteCount(Encode(compact)); var fullBytes = Encoding.UTF8.GetByteCount(Encode(full));
        check(sparseBytes * 5 < fullBytes, $"synthetic 20-sense wire response shrinks from {fullBytes} to {sparseBytes} bytes without skipping comparisons");
        check(SenseMerging.Parse(Encode(full), request).Pairs.Length == request.Pairs.Length &&
            JsonSerializer.Serialize(Groups(SenseMerging.Parse(Encode(parsed), request))) == JsonSerializer.Serialize(Groups(full)),
            "fully enumerated plans and normalized sparse cache round trips remain readable");
        var none = SenseMerging.Parse(Encode(compact with { Pairs = [] }), request);
        check(SenseMerging.Group(answers, none).Length == 20, "an empty positive list cannot hide any original sense");
        var bridge = SenseMerging.Parse(Encode(compact with { Pairs = [new("m0s0", "m0s1", "equivalent", 0.99), new("m0s1", "m0s2", "equivalent", 0.99)] }), request);
        check(SenseMerging.Group(answers, bridge).All(g => g.Variants.Length <= 2), "missing sparse edges cannot create a transitive overmerge");
        var weak = SenseMerging.Parse(Encode(compact with { Pairs = positives.Select(p => p with { Confidence = 0.7 }).ToArray() }), request);
        check(SenseMerging.Group(answers, weak).Length == 20, "low-confidence positive links remain non-merging in the decoder");
        foreach (var invalid in new[]
        {
            compact with { InputHash = "other-query" }, compact with { ComparedPairs = 189 }, compact with { ComparedPairs = -1 },
            compact with { ComparedPairs = null }, compact with { Overview = null }, compact with { Overview = ["foreign-id"] },
            compact with { Pairs = [positives[0], positives[0]] }, compact with { Pairs = [positives[0] with { A = "foreign-id" }] },
            compact with { Pairs = [positives[0] with { Confidence = 2 }] }
        })
        {
            try { SenseMerging.Parse(Encode(invalid), request); check(false, "malformed sparse response"); }
            catch (InvalidDataException) { check(true, "sparse response rejects mismatched coverage, IDs, overview and malformed links"); }
        }
        var cache = new Cache(Path.Combine(root, "sparse-" + Guid.NewGuid().ToString("N")));
        var settings = new AppSettings { Provider = model.Provider, Model = model.Model, ParallelLookup = false, MergeSenses = true };
        int judged = 0;
        var coordinator = new DictionaryCoordinator(cache, (_, _, _, _, _, _) => Task.FromResult(entry with { Senses = entry.Senses.Take(8).ToArray() }),
            (r, _, _) => { judged++; return Task.FromResult(compact with { InputHash = r.InputHash, Pairs = positives.Take(4).ToArray(), ComparedPairs = r.Pairs.Length }); });
        var result = await coordinator.LookupAsync("synthetic", null, settings, false, default);
        var restored = coordinator.Cached("synthetic", null, settings);
        check(judged == 1 && !result.MergeFailed && result.Groups.Length == 4 && restored.Groups.Length == 4 && restored.Groups.Sum(g => g.Variants.Length) == 8,
            "coordinator normalizes sparse judge output and restores the same groups from the versioned cache");
        using var prompt = JsonDocument.Parse(request.Prompt);
        check(prompt.RootElement.GetProperty("pairCount").GetInt32() == 190 && prompt.RootElement.GetProperty("pairs").GetArrayLength() == 190,
            "sparse output keeps every requested comparison in the input and states its coverage count");
    }

    public static async Task Transport(string assets, string root, Action<bool, string> check)
    {
        var first = new AiSense("noun", "A synthetic concept.", "合成概念", "", "", "", []);
        var second = first with { English = "Another wording of a synthetic concept.", Chinese = "合成概念的另一表述" };
        var answers = new[] { new ModelAnswer(new("codex", "synthetic"), ModelPhase.Complete, new("synthetic", true, [first, second], "", "unverified")) };
        var request = SenseMerging.Request("synthetic", answers);
        var plan = new SenseMergePlan(request.InputHash, [new(request.Pairs[0].A, request.Pairs[0].B, "equivalent", 0.99)], ["m0s0"], request.Pairs.Length);
        var encoded = JsonSerializer.Serialize(plan, Json.Options);
        using var client = new CompatibleTextClient(new Handler(async (http, token) =>
        {
            using var body = JsonDocument.Parse(await http.Content!.ReadAsStringAsync(token));
            var messages = body.RootElement.GetProperty("messages");
            using var payload = JsonDocument.Parse(messages[1].GetProperty("content").GetString()!);
            check(payload.RootElement.GetProperty("pairCount").GetInt32() == 1 && messages[0].GetProperty("content").GetString()!.Contains("output ONLY positive grouping judgments"),
                "compatible judge receives the sparse schema and complete comparison coverage prompt");
            string Frame(string part) => "data: " + JsonSerializer.Serialize(new { choices = new[] { new { index = 0, delta = new { content = part } } } }) + "\n\n";
            var split = encoded.Length / 2;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Frame(encoded[..split]) + Frame(encoded[split..]) + "data: [DONE]\n\n", Encoding.UTF8, "text/event-stream") };
        }));
        var settings = new AppSettings { Provider = "compatible", Model = "synthetic", CompatibleBaseUrl = "https://synthetic.example.org/v1" };
        var judge = new CompatibleDictionary(assets, client, _ => new("synthetic-id", "synthetic-secret"), root);
        var result = await judge.JudgeAsync(request, settings, default);
        check(result.ComparedPairs == 1 && SenseMerging.Group(answers, result) is [{ Variants.Length: 2 }],
            "split SSE judge output is fully validated and preserves all original variants");
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => run(request, token); }
}
