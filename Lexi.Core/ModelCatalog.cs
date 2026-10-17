namespace Lexi.Core;

public sealed record ModelChoice(string Label, string Id, string Provider = "codex")
{ public override string ToString() => Label; }

// Model options and settings rules shared by every settings screen.
public static class ModelCatalog
{
    public static ModelChoice[] Choices(AppSettings settings)
    {
        var choices = new List<ModelChoice> { new("Claude Haiku · 流式显示", "haiku", "claude"), new("GPT-6 Luna · Fast", "gpt-6-luna"),
            new("GPT-6.1 Sol · Fast", "gpt-6.1-sol"), new("Codex 默认 · Fast", "") };
        // Keep a previously saved model selectable even when it is no longer offered by default.
        foreach (var selected in new[] { LookupModel.Primary(settings), LookupModel.Secondary(settings), new LookupModel("codex", settings.JudgeModel) })
            if (!choices.Any(x => x.Id == selected.Model && x.Provider == selected.Provider)) choices.Add(new(selected.Name, selected.Model, selected.Provider));
        return choices.ToArray();
    }
    // The sense judge runs through the Codex subscription.
    public static ModelChoice[] JudgeChoices(ModelChoice[] choices) => choices.Where(c => c.Provider == "codex").ToArray();

    public static string? Validate(AppSettings settings)
    {
        if (settings.Provider is not ("codex" or "claude") || settings.SecondaryProvider is not ("" or "codex" or "claude"))
            return "请选择受支持的模型服务。";
        if (settings.ParallelLookup && LookupModel.Primary(settings) == LookupModel.Secondary(settings)) return "请选择两个不同的查询模型。";
        return null;
    }

    // Codex always runs with low reasoning on the fast tier; Claude disables extra thinking itself.
    public static void ApplyFixedOptions(AppSettings settings) { settings.ReasoningEffort = "low"; settings.ServiceTier = "fast"; }
}
