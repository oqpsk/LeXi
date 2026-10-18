using System.Text.Json;

namespace Lexi.Core;

public static class SpeechModels
{
    public static SpeechModel[] Load(string root, AppSettings settings)
    {
        var discovered = new Cache(root).Read<SpeechModel[]>("speech-models-v1", "elevenlabs");
        var known = SpeechCatalog.Models.Where(m => discovered == null || m.Provider != "elevenlabs").Concat(discovered ?? [])
            .Where(m => m != null && m.Name is { Length: > 0 and < 180 } && (m.Provider != "elevenlabs" || SpeechCatalog.ValidElevenVoice(m.Id)));
        if (settings.SpeechProvider == "elevenlabs" && SpeechCatalog.ValidElevenVoice(settings.SpeechModel))
            known = known.Append(new("elevenlabs", settings.SpeechModel, "ElevenLabs · " + settings.SpeechModel));
        return known.Take(300).DistinctBy(m => (m.Provider, m.Id)).ToArray();
    }
    public static async Task<SpeechModel[]> RefreshAsync(string root, SpeechClient client, bool online, string key, CancellationToken token)
    {
        if (!online) throw new InvalidOperationException("请先开启在线补充，再读取语音模型。");
        var models = await client.ElevenModelsAsync(key, token);
        await new Cache(root).WriteAsync("speech-models-v1", "elevenlabs", models, token);
        return models;
    }
}

public sealed partial class SpeechClient
{
    public async Task<SpeechModel[]> ElevenModelsAsync(string apiKey, CancellationToken token)
    {
        ValidateKey(apiKey);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token); stop.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.elevenlabs.io/v1/models");
            request.Headers.Add("xi-api-key", apiKey.Trim());
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop.Token);
            if (!response.IsSuccessStatusCode) throw await ElevenFailureAsync(response, stop.Token);
            using var json = JsonDocument.Parse(await ReadMetadataAsync(response.Content, 2 * 1024 * 1024, stop.Token));
            if (json.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("语音模型列表无效。");
            var models = json.RootElement.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.Object &&
                m.TryGetProperty("can_do_text_to_speech", out var supported) && supported.ValueKind == JsonValueKind.True)
                .Select(m => new SpeechModel("elevenlabs", ReadString(m, "model_id"), "ElevenLabs · " + ReadString(m, "name")))
                .Where(m => SpeechCatalog.ValidElevenVoice(m.Id) && m.Name.Length < 180 && !m.Name.Any(char.IsControl)).DistinctBy(m => m.Id).Take(200).ToArray();
            return models;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("读取语音模型超时，请重试。"); }
        catch (JsonException) { throw new InvalidDataException("语音模型列表无效。"); }
        catch (HttpRequestException) { throw new InvalidOperationException(ElevenUnreachable); }
    }
}

public sealed record SpeechProfile(string Provider, string Model, string Voice, string Name);
public static class SpeechPreferences
{
    public static void Remember(AppSettings settings)
    {
        var profile = new SpeechProfile(settings.SpeechProvider, settings.SpeechModel, settings.SpeechVoice, settings.SpeechVoiceName);
        settings.SpeechProfiles = (settings.SpeechProfiles ?? []).Where(p => p != null).Where(p => p.Provider != profile.Provider || p.Model != profile.Model).Append(profile).TakeLast(100).ToArray();
    }
}
