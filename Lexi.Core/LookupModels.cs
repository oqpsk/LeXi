using System.Text.Json;

namespace Lexi.Core;

public sealed record LookupModel(string Provider, string Model)
{
    public string Name => Provider == "claude" ? Model switch
    { "haiku" => "Claude Haiku", "sonnet" => "Claude Sonnet", "opus" => "Claude Opus", _ => "Claude " + Model } : Model switch
    { "gpt-6-luna" => "GPT-6 Luna", "gpt-6.1-sol" => "GPT-6.1 Sol", "" => "Codex 默认", _ => Model };
    public static LookupModel Primary(AppSettings settings) => new(settings.Provider, settings.Model);
    public static LookupModel Secondary(AppSettings settings) => string.IsNullOrWhiteSpace(settings.SecondaryProvider)
        ? settings.Provider == "claude" ? new("codex", "gpt-6-luna") : new("claude", "haiku")
        : new(settings.SecondaryProvider, settings.SecondaryModel);
    public static LookupModel[] Selected(AppSettings settings) =>
        (settings.ParallelLookup ? new[] { Primary(settings), Secondary(settings) } : [Primary(settings)]).Distinct().ToArray();
    public string CacheKey(string query, AppSettings settings) => JsonSerializer.Serialize(new
    { query, Provider, Model, settings.ReasoningEffort, settings.ServiceTier }, Json.Options);
}

public enum ModelPhase { Pending, Streaming, Complete, Failed, Offline }
public sealed record ModelAnswer(LookupModel Model, ModelPhase Phase, AiEntry? Entry = null, bool Cached = false, string Error = "");
public sealed record LookupSnapshot(ModelAnswer[] Answers, SenseGroup[] Groups, bool Merging = false, string MergeNote = "")
{
    public bool Provisional => Merging || Answers.Any(a => a.Phase is ModelPhase.Pending or ModelPhase.Streaming);
}

// Progress<T> posts callbacks; the coordinator needs immediate, serialized updates
// so a delayed preview cannot overwrite the final result of the same model.
internal sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
{ public void Report(T value) => action(value); }
