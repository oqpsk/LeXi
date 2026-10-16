using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lexi.Core;

public static partial class Queries
{
    public static string Normalize(string value) => Whitespace().Replace(
        value.Normalize(NormalizationForm.FormKC).Replace('’', '\'').Replace('‘', '\'').Trim(), " ").ToLowerInvariant();
    public static string? Validate(string value) => value.Length == 0 ? "输入一个单词或短语。" :
        value.Length > 100 || value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 8
            ? "请输入单词或短语，最多 100 个字符、8 个词。" :
        value.Any(char.IsControl) ? "查询内容含有控制字符。" : null;
    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
}

public sealed record LocalEntry(string Word, string Phonetic, string English, string Chinese, string Exchange, int Frequency);
public sealed record Sense(string PartOfSpeech, string Definition, string[] Examples);
public sealed record OnlineEntry(string Word, Sense[] Senses, string Url, DateTimeOffset FetchedAt);
public sealed record AiSense(string PartOfSpeech, string English, string Chinese, string Register,
    string Example, string ExampleChinese, int[] Evidence);
public sealed record AiEntry(string Word, bool Found, AiSense[] Senses, string Note, string EvidenceStatus);
public sealed record AiEnvelope(AiEntry Entry, string EvidenceHash, DateTimeOffset GeneratedAt, string Model = "", string ReasoningEffort = "low", string Provider = "codex");

public static class Json
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = false };
}

public sealed class AppSettings
{
    public bool Online { get; set; } = true;
    public bool AutoAi { get; set; } = true;
    public bool OfflineEnabled { get; set; } = true;
    public string CodexPath { get; set; } = "";
    public string Model { get; set; } = "";
    public string Provider { get; set; } = "codex";
    public string ServiceTier { get; set; } = "fast";
    public string ReasoningEffort { get; set; } = "low";
    public static AppSettings Load(string root)
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path.Combine(root, "settings.json")), Json.Options) ?? new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save(string root)
    {
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "settings.json");
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(this, Json.Options));
        File.Move(file + ".tmp", file, true);
    }
}
