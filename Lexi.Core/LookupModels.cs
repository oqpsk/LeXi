using System.Text.Json;

namespace Lexi.Core;

public sealed record LookupModel(string Provider, string Model)
{
    // A custom endpoint's model is named by its last segment ("codex/gpt-6.1-sol" is GPT-6.1 Sol), so
    // progress rows and status lines stay short; pickers show the full id (ModelCatalog.Choice).
    public string Name => Provider switch
    {
        CompatibleText.Provider => Known(Model[(Model.LastIndexOf('/') + 1)..]),
        "claude" => Model switch { "haiku" => "Claude Haiku", "sonnet" => "Claude Sonnet", "opus" => "Claude Opus", "default" => "Claude 默认", _ => "Claude " + Model },
        "anthropic" => Known(Model),
        _ => Model == "" ? "Codex 默认" : Known(Model)
    };
    private static string Known(string id) => id switch
    {
        "gpt-6-luna" => "GPT-6 Luna", "gpt-6.1-sol" => "GPT-6.1 Sol",
        "claude-haiku-5-5" => "Claude Haiku 5.5", "claude-sonnet-5-5" => "Claude Sonnet 5.5", "claude-opus-5-5" => "Claude Opus 5.5",
        _ => id
    };
    public static LookupModel Primary(AppSettings settings) => new(settings.Provider, settings.Model);
    public static LookupModel Secondary(AppSettings settings) => string.IsNullOrWhiteSpace(settings.SecondaryProvider)
        ? settings.Provider switch
        {
            "claude" => new("codex", "gpt-6-luna"), "openai" => new("anthropic", "claude-haiku-5-5"),
            "anthropic" => new("openai", "gpt-6-luna"), _ => new("claude", "haiku")
        }
        : new(settings.SecondaryProvider, settings.SecondaryModel);
    public static LookupModel[] Selected(AppSettings settings) =>
        (settings.ParallelLookup ? new[] { Primary(settings), Secondary(settings) } : [Primary(settings)]).Distinct().ToArray();
    public string CacheKey(string query, AppSettings settings)
    {
        if (InferenceOptions.Profile(settings, this) is { } profile)
            return JsonSerializer.Serialize(new ModelProfileCacheKey(query, Provider, Model, Provider == CompatibleText.Provider ? CompatibleText.Scope(settings) : "",
                profile.Effort, InferenceOptions.CanonicalSpeed(profile.Speed)), Json.Options);
        return Provider == CompatibleText.Provider
            ? JsonSerializer.Serialize(new ModelEndpointCacheKey(query, Provider, Model, CompatibleText.Scope(settings), settings.ReasoningEffort, settings.ServiceTier), Json.Options)
            : JsonSerializer.Serialize(new ModelCacheKey(query, Provider, Model, settings.ReasoningEffort, settings.ServiceTier), Json.Options);
    }
}

// The named shapes below replace anonymous objects so JSON can be generated ahead of time; their
// property order and names keep existing cache keys and hashes byte-for-byte unchanged.
internal sealed record ModelCacheKey(string Query, string Provider, string Model, string ReasoningEffort, string ServiceTier);
internal sealed record ModelEndpointCacheKey(string Query, string Provider, string Model, string Endpoint, string ReasoningEffort, string ServiceTier);
internal sealed record ModelProfileCacheKey(string Query, string Provider, string Model, string Endpoint, string Effort, string Speed);

public enum ModelPhase { Pending, Streaming, Complete, Failed, Offline }
public sealed record ModelAnswer(LookupModel Model, ModelPhase Phase, AiEntry? Entry = null, bool Cached = false, string Error = "");
public sealed record LookupSnapshot(ModelAnswer[] Answers, SenseGroup[] Groups, bool Merging = false, string MergeNote = "", bool MergeFailed = false, MergeFailureKind? MergeFailure = null)
{
    public bool Provisional => Merging || Answers.Any(a => a.Phase is ModelPhase.Pending or ModelPhase.Streaming);
}

// Progress<T> posts callbacks; the coordinator needs immediate, serialized updates
// so a delayed preview cannot overwrite the final result of the same model.
internal sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
{ public void Report(T value) => action(value); }
