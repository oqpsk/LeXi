using System.IO;
using System.Net.Http;
using System.Text.Json;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    private readonly Pronunciation pronunciations = new();
    private bool sourcePronunciation;

    private async Task LoadPronunciationAsync(string query, LocalHit[] local, CancellationToken token, bool force)
    {
        sourcePronunciation = !string.IsNullOrWhiteSpace(Phonetic.Text);
        if (sourcePronunciation)
        { Phonetic.ToolTip = "音标来源：" + local.First(h => !string.IsNullOrWhiteSpace(h.Entry.Phonetic)).Dictionary.Name; return; }
        if (Queries.HasChinese(query)) return;
        var cached = cache.Read<PronunciationEntry>("pronunciations-v1", query);
        void Apply(PronunciationEntry entry)
        {
            if (token.IsCancellationRequested || current != query) return;
            var values = entry.Ipa.Where(p => !string.IsNullOrWhiteSpace(p) && Pronunciation.Valid(p)).ToArray();
            if (values.Length == 0) return;
            sourcePronunciation = true; Phonetic.Text = string.Join(" · ", values);
            Phonetic.ToolTip = "Wiktionary 音标 · CC BY-SA 4.0\n" + entry.SourceUrl;
        }
        if (cached?.Word == query && cached.Ipa is { Length: > 0 }) Apply(cached);
        if (!settings.Online || !force && sourcePronunciation && DateTimeOffset.UtcNow - cached!.FetchedAt < TimeSpan.FromDays(30)) return;
        try
        {
            var entry = await pronunciations.LookupAsync(query, token);
            if (entry == null) return;
            token.ThrowIfCancellationRequested(); Apply(entry);
            await TryCacheAsync("pronunciations-v1", query, entry);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException) { }
    }

    private void ApplyModelPronunciation(LookupSnapshot snapshot)
    {
        if (sourcePronunciation) return;
        var answer = snapshot.Answers.FirstOrDefault(a => a.Entry is { Found: true } && !string.IsNullOrWhiteSpace(a.Entry.Phonetic));
        if (answer?.Entry is not { } entry || !Pronunciation.Valid(entry.Phonetic)) return;
        Phonetic.Text = entry.Phonetic + " · AI";
        Phonetic.ToolTip = answer.Model.Name + " 提供的音标 · 未经词典来源核验";
    }
}
