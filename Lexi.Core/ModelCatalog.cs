namespace Lexi.Core;

public sealed record ModelChoice(string Label, string Id, string Provider = "codex")
{ public override string ToString() => Label; }

// Model options and settings rules shared by every settings screen.
public static class ModelCatalog
{
    public static ModelChoice[] Choices(AppSettings settings, IEnumerable<string>? compatibleModels = null)
    {
        var choices = new List<ModelChoice> { new("Claude Haiku · 流式显示", "haiku", "claude"), new("GPT-6 Luna · Fast", "gpt-6-luna"),
            new("GPT-6.1 Sol · Fast", "gpt-6.1-sol"), new("Codex 默认 · Fast", "") };
        if (compatibleModels != null)
            choices.AddRange(compatibleModels.Where(CompatibleText.ValidModel).Distinct(StringComparer.Ordinal).Select(id => new ModelChoice("自定义 · " + id, id, CompatibleText.Provider)));
        // Keep a previously saved model selectable even when it is no longer offered by default.
        foreach (var selected in new[] { LookupModel.Primary(settings), LookupModel.Secondary(settings), new LookupModel(settings.JudgeProvider, settings.JudgeModel) })
            if (!choices.Any(x => x.Id == selected.Model && x.Provider == selected.Provider)) choices.Add(new(selected.Name, selected.Model, selected.Provider));
        return choices.ToArray();
    }
    // Codex subscription or the explicitly configured text endpoint can judge senses.
    public static ModelChoice[] JudgeChoices(ModelChoice[] choices) => choices.Where(c => c.Provider is "codex" or CompatibleText.Provider).ToArray();

    public static string? Validate(AppSettings settings)
    {
        if (settings.Provider is not ("codex" or "claude" or CompatibleText.Provider) || settings.SecondaryProvider is not ("" or "codex" or "claude" or CompatibleText.Provider) || settings.JudgeProvider is not ("codex" or CompatibleText.Provider))
            return "请选择受支持的模型服务。";
        if (LookupModel.Selected(settings).Append(new(settings.JudgeProvider, settings.JudgeModel)).Any(m => m.Provider == CompatibleText.Provider))
        {
            try { CompatibleText.NormalizeBase(settings.CompatibleBaseUrl); }
            catch (CompatibleException e) { return e.Message; }
            if (LookupModel.Selected(settings).Append(new(settings.JudgeProvider, settings.JudgeModel)).Any(m => m.Provider == CompatibleText.Provider && !CompatibleText.ValidModel(m.Model)))
                return "请先连接自定义文本服务，读取模型列表并选择模型。";
        }
        if (settings.ParallelLookup && LookupModel.Primary(settings) == LookupModel.Secondary(settings)) return "请选择两个不同的查询模型。";
        return null;
    }

    // Codex always runs with low reasoning on the fast tier; Claude disables extra thinking itself.
    public static void ApplyFixedOptions(AppSettings settings) { settings.ReasoningEffort = "low"; settings.ServiceTier = "fast"; }
}
