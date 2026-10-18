namespace Lexi.Core;

public sealed record ModelChoice(string Label, string Id, string Provider = "codex", InferenceCapabilities? Options = null, string Effort = "", string Speed = "")
{
    public InferenceCapabilities Capabilities => Options ?? InferenceOptions.Subscription(Provider, Id);
    public override string ToString() => Label;
}

// Model options and settings rules shared by every settings screen.
public static class ModelCatalog
{
    public static ModelChoice Choice(string provider, string id, InferenceCapabilities? capabilities = null) =>
        new(new LookupModel(provider, id).Name + (provider == CompatibleText.Provider ? " · 自定义 API" : " · OAuth 订阅"), id, provider, capabilities ?? (provider == CompatibleText.Provider ? InferenceOptions.Unknown : InferenceOptions.Subscription(provider, id)));
    public static ModelChoice[] Choices(AppSettings settings, IEnumerable<string>? compatibleModels = null, IEnumerable<ModelChoice>? details = null)
    {
        var choices = new List<ModelChoice> { Choice("claude", "haiku"), Choice("codex", "gpt-6-luna"), Choice("codex", "gpt-6.1-sol"), Choice("codex", "") };
        if (details != null) foreach (var model in details)
        {
            choices.RemoveAll(m => m.Provider == model.Provider && m.Id == model.Id);
            choices.Add(Choice(model.Provider, model.Id, model.Options));
        }
        if (compatibleModels != null)
            choices.AddRange(compatibleModels.Where(CompatibleText.ValidModel).Distinct(StringComparer.Ordinal).Select(id => Choice(CompatibleText.Provider, id)));
        // Keep a previously saved model selectable even when it is no longer offered by default.
        foreach (var selected in new[] { LookupModel.Primary(settings), LookupModel.Secondary(settings), new LookupModel(settings.JudgeProvider, settings.JudgeModel) })
            if (!choices.Any(x => x.Id == selected.Model && x.Provider == selected.Provider)) choices.Add(Choice(selected.Provider, selected.Model));
        return choices.Select(m => { var selected = InferenceOptions.Selected(settings, new(m.Provider, m.Id)); return m with { Effort = selected.Effort, Speed = selected.Speed }; }).ToArray();
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
        if ((settings.InferenceProfiles ?? []).Length > 1024 || (settings.InferenceProfiles ?? []).Any(p => p == null || p.Provider is not ("codex" or "claude" or CompatibleText.Provider) || p.Model == null || p.Model.Length > 256 || p.Endpoint == null || p.Endpoint.Length > 128 || !InferenceOptions.ValidEffort(p.Effort) || !InferenceOptions.ValidSpeed(p.Speed)))
            return "模型 effort 或速度配置无效，请在设置中重新选择。";
        return null;
    }

}
