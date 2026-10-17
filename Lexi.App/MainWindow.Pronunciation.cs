using System.IO;
using System.Net.Http;
using System.Text.Json;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    private readonly Pronunciation pronunciations = new();
    private bool sourcePronunciation;

    internal async Task LoadPronunciationAsync(string query, CancellationToken token, bool force, bool allowNetwork = true)
    {
        if (Queries.HasChinese(query)) return;
        var cached = cache.Read<PronunciationEntry>("pronunciations-v3", query);
        void Apply(PronunciationEntry entry)
        {
            if (token.IsCancellationRequested || current != query) return;
            if (entry.Forms is { Length: > 0 }) ApplyOnlineWordForms(query, entry.Forms);
            ApplySourceEtymology(entry.Etymologies ?? []);
            var values = entry.Ipa.Select(Pronunciation.Format).Where(p => p.Length > 0).Distinct().ToArray();
            if (values.Length == 0) return;
            sourcePronunciation = true; Phonetic.Text = string.Join(" · ", values); PhoneticLabel.Text = ""; PhoneticLabel.ToolTip = null;
            Phonetic.ToolTip = "Wiktionary 音标 · CC BY-SA 4.0\n" + entry.SourceUrl;
        }
        if (cached?.Word == query) Apply(cached);
        else if (cache.Read<PronunciationEntry>("pronunciations-v2", query) is { } legacy && legacy.Word == query) Apply(legacy);
        if (!allowNetwork || !settings.Online || !force && cached?.Word == query) return;
        try
        {
            var entry = await pronunciations.LookupAsync(query, token);
            if (entry == null) return;
            token.ThrowIfCancellationRequested(); Apply(entry);
            await TryCacheAsync("pronunciations-v3", query, entry);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException) { }
    }

    private void ApplyModelPronunciation(LookupSnapshot snapshot)
    {
        if (sourcePronunciation) return;
        var answer = snapshot.Answers.FirstOrDefault(a => a.Entry is { Found: true } && Pronunciation.Valid(a.Entry.Phonetic));
        if (answer?.Entry is not { } entry) return;
        Phonetic.Text = Pronunciation.Format(entry.Phonetic); PhoneticLabel.Text = "  AI";
        Phonetic.ToolTip = answer.Model.Name + " 提供的音标 · 未经词典资料核对";
        PhoneticLabel.ToolTip = Phonetic.ToolTip;
    }
}
