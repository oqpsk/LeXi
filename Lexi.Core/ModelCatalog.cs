namespace Lexi.Core;

public sealed record ModelChoice(string Label, string Id, string Provider = "codex", InferenceCapabilities? Options = null, string Effort = "", string Speed = "", bool Hidden = false)
{
    public InferenceCapabilities Capabilities => Options ?? InferenceOptions.Subscription(Provider, Id);
    public override string ToString() => Label;
}

// Model options and settings rules shared by every settings screen.
public static class ModelCatalog
{
    // Where the official subscription clients cannot run (iOS), models are reached with the user's
    // own API keys (OpenAI, Anthropic) instead of Codex, Claude Code or a custom text endpoint.
    public static bool ApiOnly { get; } = OperatingSystem.IsIOS();
    public static bool IsApi(string provider) => ApiKeys.Supports(provider);

    public static ModelChoice Choice(string provider, string id, InferenceCapabilities? capabilities = null, bool hidden = false)
    {
        var options = capabilities ?? (provider == CompatibleText.Provider ? InferenceOptions.Unknown : InferenceOptions.Subscription(provider, id));
        const string hiddenNote = "官方客户端隐藏模型；能否调用取决于订阅权限。";
        if (hidden && !options.Note.Contains(hiddenNote, StringComparison.Ordinal)) options = options with { Note = hiddenNote + options.Note };
        return new((provider == CompatibleText.Provider ? "API | " : "OAuth | ") + new LookupModel(provider, id).Name + (hidden ? " · 隐藏" : ""), id, provider, options, Hidden: hidden);
    }
    public static ModelChoice[] Choices(AppSettings settings, IEnumerable<string>? compatibleModels = null, IEnumerable<ModelChoice>? details = null)
    {
        var choices = new List<ModelChoice>();
        if (details != null) foreach (var model in details)
        {
            choices.RemoveAll(m => m.Provider == model.Provider && m.Id == model.Id);
            choices.Add(Choice(model.Provider, model.Id, model.Options, model.Hidden));
        }
        if (compatibleModels != null)
            choices.AddRange(compatibleModels.Where(CompatibleText.ValidModel).Distinct(StringComparer.Ordinal).Select(id => Choice(CompatibleText.Provider, id)));
        // Keep a previously saved model selectable even when it is no longer offered by default.
        foreach (var selected in new[] { LookupModel.Primary(settings), LookupModel.Secondary(settings), new LookupModel(settings.JudgeProvider, settings.JudgeModel) })
            if (!choices.Any(x => x.Id == selected.Model && x.Provider == selected.Provider)) choices.Add(Choice(selected.Provider, selected.Model));
        return choices.Select(m => { var selected = InferenceOptions.Selected(settings, new(m.Provider, m.Id)); return m with { Effort = selected.Effort, Speed = selected.Speed }; }).ToArray();
    }
    // The models offered with API keys; a previously saved API model stays selectable.
    public static ModelChoice[] ApiChoices(AppSettings settings)
    {
        var choices = new List<ModelChoice> { new("GPT-6.1 Sol · OpenAI API", "gpt-6.1-sol", "openai"), new("GPT-6 Luna · OpenAI API", "gpt-6-luna", "openai"),
            new("Claude Haiku 5.5 · Anthropic API", "claude-haiku-5-5", "anthropic"), new("Claude Sonnet 5.5 · Anthropic API", "claude-sonnet-5-5", "anthropic") };
        foreach (var selected in new[] { LookupModel.Primary(settings), LookupModel.Secondary(settings), Judge(settings, true) })
            if (IsApi(selected.Provider) && !choices.Any(x => x.Id == selected.Model && x.Provider == selected.Provider))
                choices.Add(new(selected.Name, selected.Model, selected.Provider));
        return choices.ToArray();
    }
    // Codex subscription, the explicitly configured text endpoint or either API service can judge senses.
    public static ModelChoice[] JudgeChoices(ModelChoice[] choices) => choices.Where(c => c.Provider is "codex" or CompatibleText.Provider || IsApi(c.Provider)).ToArray();
    // With API keys the judge's service follows its model id; elsewhere it is the saved judge provider.
    public static LookupModel Judge(AppSettings settings) => Judge(settings, ApiOnly);
    public static LookupModel Judge(AppSettings settings, bool api) =>
        !api ? new(settings.JudgeProvider, settings.JudgeModel) : new(settings.JudgeModel.StartsWith("claude", StringComparison.Ordinal) ? "anthropic" : "openai", settings.JudgeModel);

    public static string? Validate(AppSettings settings) => Validate(settings, ApiOnly);
    public static string? Validate(AppSettings settings, bool api)
    {
        if (api)
        {
            if (!IsApi(settings.Provider) || settings.SecondaryProvider.Length > 0 && !IsApi(settings.SecondaryProvider))
                return "请选择受支持的模型服务。";
            if (new[] { settings.Model, settings.SecondaryModel, settings.JudgeModel }.Any(m => m.Length > 0 && !ValidModelId(m)) ||
                settings.Model.Length == 0 || settings.JudgeModel.Length == 0)
                return "请填写有效的模型 ID。";
        }
        else
        {
            if (settings.Provider is not ("codex" or "claude" or CompatibleText.Provider) || settings.SecondaryProvider is not ("" or "codex" or "claude" or CompatibleText.Provider) || settings.JudgeProvider is not ("codex" or CompatibleText.Provider))
                return "请选择受支持的模型服务。";
            if (LookupModel.Selected(settings).Append(new(settings.JudgeProvider, settings.JudgeModel)).Any(m => m.Provider == CompatibleText.Provider))
            {
                try { CompatibleText.NormalizeBase(settings.CompatibleBaseUrl); }
                catch (CompatibleException e) { return e.Message; }
                if (LookupModel.Selected(settings).Append(new(settings.JudgeProvider, settings.JudgeModel)).Any(m => m.Provider == CompatibleText.Provider && !CompatibleText.ValidModel(m.Model)))
                    return "请先完成 API 连接，读取模型列表并选择模型。";
            }
        }
        if (settings.ParallelLookup && LookupModel.Primary(settings) == LookupModel.Secondary(settings)) return "请选择两个不同的查询模型。";
        if ((settings.InferenceProfiles ?? []).Length > 1024 || (settings.InferenceProfiles ?? []).Any(p => p == null || p.Provider is not ("codex" or "claude" or CompatibleText.Provider) || p.Model == null || p.Model.Length > 256 || p.Endpoint == null || p.Endpoint.Length > 128 || !InferenceOptions.ValidEffort(p.Effort) || !InferenceOptions.ValidSpeed(p.Speed)))
            return "模型 effort 或速度配置无效，请在设置中重新选择。";
        return null;
    }
    public static bool ValidModelId(string id) => id.Length is > 0 and <= 100 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or ':');

    // API platforms start from an OpenAI model with an Anthropic second opinion; desktop settings
    // copied over keep nothing that needs a subscription client.
    public static void UseApiDefaults(AppSettings settings)
    {
        if (!IsApi(settings.Provider) || !ValidModelId(settings.Model)) { settings.Provider = "openai"; settings.Model = "gpt-6-luna"; }
        if (settings.SecondaryProvider.Length > 0 && (!IsApi(settings.SecondaryProvider) || !ValidModelId(settings.SecondaryModel)))
        { settings.SecondaryProvider = ""; settings.SecondaryModel = ""; }
        if (!ValidModelId(settings.JudgeModel)) settings.JudgeModel = "gpt-6-luna";
    }
}
