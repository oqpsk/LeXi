using System.IO;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    private SavedQueries SavedResults => new(cache);

    private void KeepCurrentResult(string message)
    {
        if (QueryProgress.Current is { Busy: true } progress)
            QueryProgress.Begin(progress.Interrupted("查询已停止", "设置已更改 · 点击重新生成可更新"));
        LookupStatus.Text = message + " · 已保留当前结果" + (settings.Online ? " · 重新生成可更新" : "");
    }

    private bool RestoreDictionary(string query, bool force, long request, LookupTiming timing)
    {
        if (SavedResults.Dictionary(query, force) is not { } saved) return false;
        RenderSavedDictionary(saved, request, timing); return true;
    }

    internal void RenderSavedDictionary(SavedDictionary saved, long request, LookupTiming timing)
    {
        var query = saved.Query;
        current = query;
        ApplyLocalWordForms(query, saved.LocalForms); ApplyOnlineWordForms(query, saved.OnlineForms);
        RenderLocal(saved.Local, query); BeginLexicalOverview(query, saved.Local);
        ApplySourceEtymology(saved.Etymologies);
        Phonetic.Text = saved.Pronunciation.Text; PhoneticLabel.Text = saved.Pronunciation.Label;
        Phonetic.ToolTip = saved.Pronunciation.Attribution; PhoneticLabel.ToolTip = saved.Pronunciation.LabelAttribution;
        sourcePronunciation = saved.Pronunciation.Sourced;
        if (saved.Online != null) RenderOnline(saved.Online, true);
        RenderAnswers(saved.Result, saved.Evidence);
        timing.MarkFirst();
        var state = saved.Result.Answers.Length > 0 ? QueryProgressState.Finished(saved.Result, saved.MergeEnabled,
            saved.SourceIncomplete, SpecialistReferences.Uncovered(query, saved.Result.Groups, saved.Evidence).Length) :
            QueryProgressState.SourcesFinished(false, saved.SourceIncomplete);
        QueryProgress.Update(request, state with { Detail = "已恢复保存的结果 · 点击重新生成可更新" });
        LookupStatus.Text = timing.Finish(state.Warning ? "已显示缓存 · 部分结果 · 重新生成可重试" : "已显示缓存 · 重新生成可更新");
    }

    private async Task SaveDictionaryAsync(string query, LocalHit[] local, OnlineEntry? online, OnlineEntry? evidence,
        LookupSnapshot result, bool merge, bool incomplete, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var saved = new SavedDictionary(query, local, localWordForms, onlineWordForms, sourceEtymologies, online, evidence, result,
            new(Phonetic.Text, PhoneticLabel.Text, Phonetic.ToolTip?.ToString() ?? "", PhoneticLabel.ToolTip?.ToString() ?? "", sourcePronunciation),
            merge, incomplete, DateTimeOffset.UtcNow);
        try { await SavedResults.SaveDictionaryAsync(saved, token); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Keep the displayed result if persistence is unavailable. */ }
    }

    private async Task SaveTranslationAsync(string text, string target, TranslationEntry entry, LookupModel model, CancellationToken token)
    {
        try { await SavedResults.SaveTranslationAsync(text, target, new(entry, model, DateTimeOffset.UtcNow), token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
