using System.Net;
using System.Text;
using System.Text.Json;
using Lexi.Core;

internal static class InferenceChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        const string endpoint = "https://models.example.org/v1";
        using var payload = JsonDocument.Parse("""{"supported_reasoning_levels":[{"effort":"low"},{"effort":"high"},{"effort":"ultra"},{"effort":"invalid"}],"service_tiers":[{"id":"priority","name":"Fast"},{"id":"ultrafast"},{"id":"unknown"}]}""");
        var caps = InferenceOptions.Parse(payload.RootElement);
        check(caps.Efforts.SequenceEqual(["low", "high", "ultra"]) && caps.Speeds.Select(s => s.Id).SequenceEqual(["default", "priority", "ultrafast"]), "capabilities follow server metadata and reject unknown parameter values");
        using var other = JsonDocument.Parse("""{"supportedReasoningEfforts":[{"reasoningEffort":"medium"}],"serviceTiers":[{"id":"fast"}]}""");
        var official = InferenceOptions.Parse(other.RootElement);
        check(official.Efforts.SequenceEqual(["medium"]) && official.Speeds[1].Id == "priority", "official model-list camelCase metadata and Fast alias normalize consistently");
        using var claude = JsonDocument.Parse("""{"supportedEffortLevels":["low","medium","high"],"supportsFastMode":true}""");
        var claudeCaps = InferenceOptions.Parse(claude.RootElement);
        check(claudeCaps.Efforts.SequenceEqual(["low", "medium", "high"]) && claudeCaps.Speeds.Last().Id == "priority", "Claude official initialization capabilities drive the same effort and speed controls");
        check(InferenceOptions.ValidateSelection(caps, "medium", "priority") != null && InferenceOptions.ValidateSelection(caps, "high", "ultrafast") == null,
            "effort and speed are independently validated without choosing a lower supported value");
        check(InferenceOptions.Unknown.EffortChoices.Length == 1 && InferenceOptions.Unknown.SpeedChoices.Length == 1 &&
            InferenceOptions.ValidateSelection(InferenceOptions.Unknown, "low", "") != null, "unknown capabilities offer defaults only, without guessing from model names");
        var settings = new AppSettings { Provider = "compatible", Model = "model-a", CompatibleBaseUrl = endpoint };
        var model = ModelCatalog.Choice("compatible", "model-a", caps);
        var original = settings.ForModel(LookupModel.Primary(settings));
        var initialKey = LookupModel.Primary(settings).CacheKey("Word", settings); var initialTranslation = Translation.Key("Text.", "zh", settings);
        InferenceOptions.Configure(settings, model, "high", "ultrafast");
        check(InferenceOptions.Selected(settings, LookupModel.Primary(settings)) == new InferenceSelection("high", "ultrafast") &&
            InferenceOptions.Selected(original, LookupModel.Primary(original)) == new InferenceSelection("", ""), "profile changes remain isolated from query snapshots and settings drafts");
        check(initialKey != LookupModel.Primary(settings).CacheKey("Word", settings) && initialTranslation != Translation.Key("Text.", "zh", settings), "dictionary and translation cache identities include explicit effort and speed");
        var anotherModel = settings.ForModel(new("compatible", "model-b")); var anotherProvider = settings.ForModel(new("codex", "model-a"));
        var anotherEndpoint = settings.ForModel(LookupModel.Primary(settings)); anotherEndpoint.CompatibleBaseUrl = "https://other.example.org/v1";
        check(InferenceOptions.Profile(anotherModel, LookupModel.Primary(anotherModel)) == null && InferenceOptions.Profile(anotherProvider, LookupModel.Primary(anotherProvider)) == null &&
            InferenceOptions.Profile(anotherEndpoint, LookupModel.Primary(anotherEndpoint)) == null, "profiles distinguish model ID, OAuth versus API, and complete endpoint");
        InferenceOptions.Configure(settings, model, "low", "priority");
        check(settings.InferenceProfiles.Length == 1 && InferenceOptions.Selected(settings, new("compatible", "model-a")).Effort == "low", "changing a model's options replaces only its own saved profile");
        try { InferenceOptions.Configure(settings, model, "medium", "priority"); check(false, "unsupported option"); }
        catch (InvalidOperationException) { check(InferenceOptions.Selected(settings, new("compatible", "model-a")).Effort == "low", "rejected option leaves the last explicit selection intact"); }
        var legacy = new AppSettings();
        check(InferenceOptions.Selected(legacy, LookupModel.Primary(legacy)) == new InferenceSelection("low", "priority") &&
            LookupModel.Primary(legacy).CacheKey("Word", legacy) == JsonSerializer.Serialize(new { query = "Word", legacy.Provider, legacy.Model, legacy.ReasoningEffort, legacy.ServiceTier }, Lexi.Core.Json.Options),
            "legacy OAuth Low/Fast and cache identities survive the upgrade until explicitly changed");
        var folder = Path.Combine(root, "inference", Guid.NewGuid().ToString("N"));
        await InferenceOptions.Remember(folder, "compatible", endpoint, [model]);
        var restored = InferenceOptions.Load(folder, "compatible", endpoint + "/");
        check(restored.Length == 1 && restored[0].Capabilities.Efforts.SequenceEqual(caps.Efforts) &&
            InferenceOptions.Load(folder, "compatible", "https://other.example.org/v1").Length == 0, "public capability cache survives reopening and is isolated by normalized endpoint");
        var choices = InferenceOptions.Available(folder, settings);
        check(choices.Any(c => c.Provider == "compatible" && c.Id == "model-a" && c.Label.StartsWith("API | ", StringComparison.Ordinal) && c.Speed == "priority") &&
            choices.Where(c => c.Provider != "compatible").All(c => c.Label.StartsWith("OAuth | ", StringComparison.Ordinal) && !c.Label.Contains("Fast")), "model menus label connection mode and keep speed outside model names");
        InferenceOptions.ValidateRequest(settings, folder);
        await InferenceOptions.Remember(folder, "compatible", endpoint, [model with { Options = InferenceOptions.Unknown }]);
        try { InferenceOptions.ValidateRequest(settings, folder); check(false, "removed capability"); }
        catch (InvalidOperationException) { check(true, "removed capabilities reject the next explicit request without a silent downgrade"); }
        int calls = 0;
        using var client = new CompatibleTextClient(new Handler(async (request, token) =>
        {
            calls++; using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var json = body.RootElement;
            if (calls == 1) check(json.GetProperty("reasoning_effort").GetString() == "high" && json.GetProperty("service_tier").GetString() == "priority" && json.GetProperty("stream").GetBoolean(), "streaming Chat Completions sends the exact selected reasoning_effort and service_tier");
            else check(!json.TryGetProperty("reasoning_effort", out _) && !json.TryGetProperty("service_tier", out _), "default inference settings omit both optional parameters in ordinary JSON requests");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"choices":[{"message":{"content":"ok"},"finish_reason":"stop"}]}""", Encoding.UTF8, "application/json") };
        }));
        var credentials = new CompatibleCredentials("synthetic-id", "synthetic-secret");
        await client.CompleteAsync(endpoint, "model-a", true, credentials, "test", "test", default, effort: "high", speed: "priority");
        await client.CompleteAsync(endpoint, "model-a", false, credentials, "test", "test", default);
        int rejectedCalls = 0;
        using var rejected = new CompatibleTextClient(new Handler((_, _) => { rejectedCalls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("private provider body") }); }));
        try { await rejected.CompleteAsync(endpoint, "model-a", true, credentials, "test", "test", default, effort: "low", speed: "priority"); check(false, "rejected speed"); }
        catch (CompatibleException e) { check(e.Code == "invalid_options" && e.Message.Contains("effort") && !e.Message.Contains("private") && rejectedCalls == 1, "unsupported request parameters produce an actionable sanitized error with no retry or downgrade"); }
        settings.Save(folder);
        check(AppSettings.Load(folder).InferenceProfiles.Single().Effort == "low", "model-specific inference preferences persist across app restarts");
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => run(request, token); }
}
