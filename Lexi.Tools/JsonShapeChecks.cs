using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Lexi.Core;
using Lexi.Host;

// Core and the host serialize only types generated at build time, so they also run under NativeAOT
// (iOS). Each named shape that replaced an anonymous object must write the bytes the anonymous
// object wrote, so saved cache keys, hashes, retry checkpoints, backups, requests and protocol
// replies keep matching. The anonymous side below is the earlier code, serialized by reflection.
internal static class JsonShapeChecks
{
    private static readonly JsonSerializerOptions Before = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = false, TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
    private static readonly JsonSerializerOptions HostBefore = new(Before)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static async Task Run(string work)
    {
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        int count = 0;
        void Same(string named, string anonymous, string name)
        {
            if (named != anonymous) throw new Exception($"FAIL: {name}\n  named:     {named}\n  anonymous: {anonymous}");
            Console.WriteLine("PASS: " + name); count++;
        }
        static string Old(object value) => JsonSerializer.Serialize(value, Before);
        static string New<T>(T value) => JsonSerializer.Serialize(value, Json.Options);

        // Quotes, markup, Chinese and an astral character keep the encoder in every comparison.
        const string query = "Ab \"c\" <d> & 中文 😀";
        const string endpoint = "https://models.example.org/v1";
        var codex = new AppSettings { Provider = "codex", Model = "gpt-6-luna", ReasoningEffort = "medium", ServiceTier = "fast" };
        var compatible = new AppSettings { Provider = CompatibleText.Provider, Model = "model-a", CompatibleBaseUrl = endpoint };
        var capabilities = new InferenceCapabilities(["low", "high"], [new("default", "标准"), new("priority", "Fast")]);
        var codexProfiled = codex.ForModel(LookupModel.Primary(codex));
        InferenceOptions.Configure(codexProfiled, ModelCatalog.Choice("codex", "gpt-6-luna", capabilities), "high", "fast");
        var compatibleProfiled = compatible.ForModel(LookupModel.Primary(compatible));
        InferenceOptions.Configure(compatibleProfiled, ModelCatalog.Choice(CompatibleText.Provider, "model-a", capabilities), "low", "");

        foreach (var settings in new[] { codex, compatible, codexProfiled, compatibleProfiled })
        {
            var model = LookupModel.Primary(settings);
            var custom = model.Provider == CompatibleText.Provider;
            var kind = (custom ? "custom endpoint" : "subscription") + (settings.InferenceProfiles.Length > 0 ? " with per-model effort and speed" : "");
            var expected = InferenceOptions.Profile(settings, model) is { } profile
                ? Old(new { query, model.Provider, model.Model, endpoint = custom ? CompatibleText.Scope(settings) : "", profile.Effort, speed = InferenceOptions.CanonicalSpeed(profile.Speed) })
                : custom ? Old(new { query, model.Provider, model.Model, endpoint = CompatibleText.Scope(settings), settings.ReasoningEffort, settings.ServiceTier })
                : Old(new { query, model.Provider, model.Model, settings.ReasoningEffort, settings.ServiceTier });
            Same(model.CacheKey(query, settings), expected, "dictionary cache key, " + kind);

            string text = query, target = "en";
            expected = InferenceOptions.Profile(settings, model) is { } p
                ? Old(new { version = 2, text, target, settings.Provider, settings.Model, endpoint = custom ? CompatibleText.Scope(settings) : "", p.Effort, speed = InferenceOptions.CanonicalSpeed(p.Speed) })
                : custom ? Old(new { version = 1, text, target, settings.Provider, settings.Model, endpoint = CompatibleText.Scope(settings), settings.ReasoningEffort })
                : Old(new { version = 1, text, target, settings.Provider, settings.Model, settings.ReasoningEffort });
            Same(Translation.Key(text, target, settings), expected, "translation cache key, " + kind);

            expected = InferenceOptions.Profile(settings, model) is { } q ? Old(new { q.Effort, speed = InferenceOptions.CanonicalSpeed(q.Speed) }) : "";
            Same(InferenceOptions.Identity(settings, model), expected, "sense-merge inference identity, " + kind);
        }

        var request = new SenseMergeRequest(query, [
            new("m0s0", new("codex", "gpt-6-luna"), new("noun", "A \"quoted\" sense", "义项", "", "An example.", "例句。", [1, 2], Expansion: "Abbreviation"), "source_based", false),
            new("m1s0", new("claude", "haiku"), new("verb", "Another sense", "另一个义项", "slang", "", "", []), "unverified", true)], [new("m0s0", "m1s0")]);
        Same(request.InputHash, Cache.Hash(Old(new
        {
            version = 6, request.Query,
            Candidates = request.Candidates.Select(c => new
            {
                c.Id, c.Model,
                Sense = new { c.Sense.PartOfSpeech, c.Sense.English, c.Sense.Chinese, c.Sense.Register,
                    c.Sense.Example, c.Sense.ExampleChinese, c.Sense.Evidence, c.Sense.LexicalForm, c.Sense.Expansion },
                c.EvidenceStatus, c.Provisional
            }),
            request.Pairs
        })), "sense-merge input hash");
        Same(request.Prompt, Old(new
        {
            task = "compare_senses", inputHash = request.InputHash, query = request.Query, pairCount = request.Pairs.Length,
            senses = request.Candidates.Select(c => new { c.Id, c.Sense.PartOfSpeech, c.Sense.English, c.Sense.Chinese, c.Sense.Register, c.Sense.Example, c.Sense.LexicalForm, c.Sense.Expansion }),
            pairs = request.Pairs
        }), "sense-merge prompt");

        // The query identity is hashed into saved retry checkpoints.
        var paths = new LexiPaths(Path.Combine(work, "assets"), Path.Combine(work, "data"), Path.Combine(work, "user"));
        using (var session = new QuerySession(paths, Task.FromResult(new OfflineLibrary(paths.DataRoot))))
        {
            var s = session.Settings;
            s.DisabledDictionaries = ["ecdict", "moe"]; s.CodexPath = "/opt/codex \"x\""; s.SecondaryProvider = CompatibleText.Provider; s.SecondaryModel = "model-a";
            s.CompatibleBaseUrl = endpoint; s.InferenceProfiles = compatibleProfiled.InferenceProfiles;
            Same(session.QuerySettingsIdentity(), Old(new
            {
                s.Online, s.AutoAi, s.OfflineEnabled, s.DisabledDictionaries, s.CodexPath,
                s.Provider, s.Model, s.SecondaryProvider, s.SecondaryModel, s.ParallelLookup,
                s.MergeSenses, s.JudgeProvider, s.JudgeModel, s.CompatibleBaseUrl,
                s.CompatibleStreaming, s.InferenceProfiles, s.ReasoningEffort, s.ServiceTier
            }), "query settings identity behind retry checkpoints");

            var archive = Path.Combine(work, "backup.lexibackup");
            await new UserData(paths.UserRoot).ExportAsync(archive, s, session.History);
            using var zip = ZipFile.OpenRead(archive);
            using var manifest = new StreamReader(zip.GetEntry("manifest.json")!.Open(), Encoding.UTF8);
            Same(await manifest.ReadToEndAsync(), Old(new { product = "LeXi", version = 1 }), "backup manifest");
        }

        var bodies = new List<string>();
        using (var client = new CompatibleTextClient(new Capture(bodies)))
        {
            var credentials = new CompatibleCredentials("synthetic-id", "synthetic-secret");
            const string instructions = "Return JSON \"only\".", prompt = "Define <term> & 中文 😀";
            await client.CompleteAsync(endpoint, "model-a", false, credentials, instructions, prompt, default);
            await client.CompleteAsync(endpoint, "vendor/model-b", true, credentials, instructions, prompt, default, effort: "high", speed: "priority");
            var messages = new[] { new { role = "system", content = instructions }, new { role = "user", content = prompt } };
            Same(bodies[0], Old(new Dictionary<string, object> { ["model"] = "model-a", ["messages"] = messages, ["stream"] = false }),
                "custom endpoint chat request without effort or speed");
            Same(bodies[1], Old(new Dictionary<string, object> { ["model"] = "vendor/model-b", ["messages"] = messages, ["stream"] = true, ["reasoning_effort"] = "high", ["service_tier"] = "priority" }),
                "custom endpoint chat request with effort and speed");
        }

        Same(New(new ClaudeCliSettings(true, false)), Old(new { disableAllHooks = true, fastMode = false }), "Claude Code settings argument");
        Same(New(new ClaudeControlRequest("control_request", "lexi-models", new("initialize"))),
            Old(new { type = "control_request", request_id = "lexi-models", request = new { subtype = "initialize" } }), "Claude Code model catalog request");
        Same(New(new CodexInitialize(1, "initialize", new(new("LeXi", "1")))),
            Old(new { id = 1, method = "initialize", @params = new { clientInfo = new { name = "LeXi", version = "1" } } }), "Codex app-server initialization");
        Same(New(new CodexNotification("initialized")), Old(new { method = "initialized" }), "Codex app-server notification");
        foreach (var cursor in new[] { null, "next \"page\"" })
            Same(New(new CodexModelList(2, "model/list", new(cursor, true))),
                Old(new { id = 2, method = "model/list", @params = new { cursor, includeHidden = true } }), "Codex app-server model list page" + (cursor == null ? "" : " with a cursor"));

        await HostReplies(work, Same);
        Console.WriteLine($"{count} JSON shape checks passed");
    }

    // The host's replies, as the macOS and iOS apps read them, against the anonymous replies they replaced.
    private static async Task HostReplies(string work, Action<string, string, string> same)
    {
        var paths = new LexiPaths(Path.Combine(work, "assets"), Path.Combine(work, "data"), Path.Combine(work, "host"));
        using var session = new QuerySession(paths, Task.FromResult(new OfflineLibrary(paths.DataRoot)));
        session.History.Record("Serendipity \"x\"", false); session.History.Record("中文 段落。", true); session.History.Record("serene", false);
        var settings = session.Settings;
        var models = InferenceOptions.Available(paths.UserRoot, settings);
        var expected = new Dictionary<int, (string Name, object Result)>
        {
            [1] = ("settings reply", new
            {
                settings, models, judgeModels = ModelCatalog.JudgeChoices(models),
                controls = new { onlineLabel = ControlText.OnlineLabel(settings.Online), onlineTooltip = ControlText.OnlineTooltip(settings.Online),
                    regenerateTooltip = ControlText.RegenerateTooltip(settings.Online) }
            }),
            [2] = ("history page reply", History(session, "ser", 0)),
            [3] = ("suggestion reply", new { suggestions = session.Suggest("ser") }),
            [4] = ("speech model reply", new { models = SpeechModels.Load(paths.UserRoot, settings).Select(m => new { m.Provider, m.Id, m.Name, local = SpeechCatalog.IsLocal(m.Provider) }) }),
            [5] = ("empty reply", new { }),
        };
        var input = string.Join("\n", new[]
        {
            """{"id":1,"method":"settings.get"}""", """{"id":2,"method":"history.list","params":{"text":"ser"}}""",
            """{"id":3,"method":"suggest","params":{"text":"ser"}}""", """{"id":4,"method":"speech.models"}""",
            """{"id":5,"method":"text.credentials","params":{"baseUrl":"https://models.example.org/v1","apiKey":"synthetic"}}"""
        }) + "\n";
        var output = new StringWriter();
        await new HostServer(session, new StringReader(input), output).RunAsync();
        var replies = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ToDictionary(line => { using var json = JsonDocument.Parse(line); return json.RootElement.GetProperty("id").GetInt32(); });
        foreach (var (id, (name, result)) in expected)
            same(replies[id], JsonSerializer.Serialize(new { id, result }, HostBefore), "host " + name);
    }

    private static object History(QuerySession session, string text, int offset) => new
    {
        total = session.History.Entries.Count(e => e.Query.Contains(text, StringComparison.OrdinalIgnoreCase)),
        entries = session.History.Search(text, offset).Select(e => new { e.Query, e.Translation, e.QueriedAt, e.Preview, e.Kind }).ToArray(),
        error = session.History.Error
    };

    private sealed class Capture(List<string> bodies) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(token));
            return new(HttpStatusCode.OK)
            { Content = new StringContent("""{"choices":[{"index":0,"finish_reason":"stop","message":{"content":"{}"}}]}""", Encoding.UTF8, "application/json") };
        }
    }
}
