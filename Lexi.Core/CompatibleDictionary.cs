namespace Lexi.Core;

public sealed class CompatibleDictionary(string assets, CompatibleTextClient client, Func<string, CompatibleCredentials> credentials, string userRoot)
{
    private Task<string> Run(string prompt, string schema, string instructions, AppSettings settings, CancellationToken token, Action<string>? preview = null)
    {
        InferenceOptions.ValidateRequest(settings, userRoot);
        var selected = InferenceOptions.Selected(settings, LookupModel.Primary(settings));
        return client.CompleteAsync(settings.CompatibleBaseUrl, settings.Model, settings.CompatibleStreaming, credentials(settings.CompatibleBaseUrl),
            File.ReadAllText(Path.Combine(assets, instructions)) + "\nReturn one complete JSON object only, using this schema:\n" + File.ReadAllText(Path.Combine(assets, schema)),
            prompt, token, preview, selected.Effort, selected.Speed);
    }
    public async Task<AiEntry> LookupAsync(string query, OnlineEntry? evidence, AppSettings settings, CancellationToken token, IProgress<AiEntry> preview)
    {
        var updates = new StreamingPreview<AiEntry>(text => StreamingEntry.Read(text, query), preview, token);
        var result = await Run(CodexDictionary.DefineRequest(query, evidence), "dictionary.schema.json", "dictionary.instructions.txt", settings, token, updates.Report);
        try { return CodexDictionary.ParseAndValidate(StreamingEntry.Unwrap(result), query, evidence, true); }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException)
        { throw new CompatibleException("invalid_result", "自定义文本模型未返回完整、匹配的词条，请重新生成。"); }
    }
    public async Task<TranslationEntry> TranslateAsync(string text, string target, AppSettings settings, CancellationToken token, IProgress<TranslationEntry> preview)
    {
        var updates = new StreamingPreview<TranslationEntry>(json => Translation.Preview(json, text, target), preview, token);
        var result = await Run(Translation.Request(text, target), "translation.schema.json", "dictionary.instructions.txt", settings, token, updates.Report);
        try { return Translation.Parse(result, text, target); }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException)
        { throw new CompatibleException("invalid_result", "自定义文本模型未返回完整、匹配的翻译，请重试。"); }
    }
    public async Task<SenseMergePlan> JudgeAsync(SenseMergeRequest request, AppSettings settings, CancellationToken token)
    {
        var result = await Run(request.Prompt, "sense-merge.schema.json", "sense-merge.instructions.txt", settings, token);
        try { return SenseMerging.Parse(StreamingEntry.Unwrap(result), request); }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException)
        { throw new CompatibleException("invalid_result", "自定义文本模型未返回有效的义项整理结果，已保留全部释义。"); }
    }
}
