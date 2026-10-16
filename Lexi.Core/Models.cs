using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace Lexi.Core;

public static partial class Queries
{
    public static string Normalize(string value) => Whitespace().Replace(
        value.Normalize(NormalizationForm.FormKC).Replace('’', '\'').Replace('‘', '\'').Trim(), " ").ToLowerInvariant();
    public const int MaxLength = 5000;
    public static string? Validate(string value) => string.IsNullOrWhiteSpace(value) ? "输入单词、短语或待翻译文本。" :
        value.Length > MaxLength ? "每次最多输入 5000 个字符。" :
        value.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t') ? "内容含有不支持的控制字符。" : null;
    public static bool HasChinese(string text) => text.Any(c => c is >= '\u3400' and <= '\u9fff');
    public static bool IsText(string text) => text.Contains('\n') || text.Length > 100 ||
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length > 8 ||
        text.Any(c => "。！？；!?;".Contains(c)) || text.TrimEnd().EndsWith('.') ||
        text.Count(c => c is >= '\u3400' and <= '\u9fff') > 12;
    // Convert only the user's search key. Original dictionary text stays unchanged.
    public static string TraditionalKey(string text)
        => ChineseKey(text, 0x04000000);
    public static string SimplifiedKey(string text)
        => ChineseKey(text, 0x02000000);
    private static string ChineseKey(string text, uint flags)
    {
        if (!OperatingSystem.IsWindows() || !HasChinese(text)) return text;
        var output = new StringBuilder(text.Length * 2 + 1);
        return LCMapStringEx("zh-CN", flags, text, -1, output, output.Capacity, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) > 0 ? output.ToString() : text;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int LCMapStringEx(string locale, uint flags, string source, int sourceLength,
        StringBuilder destination, int destinationLength, IntPtr version, IntPtr reserved, IntPtr sortHandle);
    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
}

public sealed record LocalEntry(string Word, string Phonetic, string English, string Chinese, string Exchange, int Frequency, string Original = "");
public sealed record Sense(string PartOfSpeech, string Definition, string[] Examples, string SourceName = "", string SourceUrl = "");
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
    public string[] DisabledDictionaries { get; set; } = [];
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
