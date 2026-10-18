using System.IO.Compression;
using System.Text.Json;

namespace Lexi.Core;

public sealed record CacheUsage(string Id, string Name, long Bytes, int Files);
// A refusal worded for the user. Front ends show its message; it never contains a path or file content.
public sealed class UserDataException(string message) : InvalidOperationException(message);
public sealed record BackupPreview(int Version, int History, int Results, bool HasSettings);

// Only named application-owned categories are addressable. Keys, OAuth clients, raw dictionaries,
// audio and machine-specific window placements never enter a migration archive.
public sealed class UserData(string root)
{
    private static readonly string[] Intermediate = ["online-v2", "online-v3", "pronunciations-v2", "pronunciations-v3",
        "ai-v3", "ai-v4", "ai-v5", "ai-v6", "sense-merge-v1", "sense-merge-v2", "sense-merge-v3", "sense-merge-v4", "sense-merge-v5", "sense-merge-v6", "sense-merges-v1", "sense-merges-v2", "sense-merges-v3", "sense-merges-v4", "sense-merges-v5", "sense-merges-v6", "translations-v1", "word-forms-v1", "word-forms-v2"];
    private static readonly string[] Results = [SavedQueries.DictionaryKind, SavedQueries.TranslationKind];
    private static string[] Folders(string id) => id switch
    { "intermediate" => Intermediate, "speech" => ["speech-cache"], "results" => Results, _ => throw new UserDataException("未知缓存类别。") };
    private string Folder(string name)
    {
        var full = Path.GetFullPath(Path.Combine(root, name));
        if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UserDataException("数据路径无效。");
        if (Directory.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new UserDataException("数据目录不能是链接。");
        return full;
    }
    private IEnumerable<string> Files(string[] folders) => folders.Select(Folder).Where(Directory.Exists)
        .SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.TopDirectoryOnly))
        .Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0);
    public CacheUsage[] Usage() => new[] { ("intermediate", "中间缓存"), ("speech", "语音缓存"), ("results", "已保存查询") }
        .Select(c => { var files = Files(Folders(c.Item1)).ToArray(); return new CacheUsage(c.Item1, c.Item2, files.Sum(p => new FileInfo(p).Length), files.Length); }).ToArray();
    public void Clear(string id)
    {
        foreach (var file in Files(Folders(id)).Where(p => !p.EndsWith(".tmp", StringComparison.Ordinal))) File.Delete(file);
    }
    private static bool Allowed(string name) => name is "settings.json" or "history.json" ||
        Results.Any(k => name.StartsWith(k + "/", StringComparison.Ordinal) && name.Length == k.Length + 70 &&
            name.EndsWith(".json", StringComparison.Ordinal) && name[(k.Length + 1)..^5].All(c => char.IsAsciiHexDigit(c) && !char.IsLower(c)));
    public async Task ExportAsync(string destination, AppSettings settings, QueryHistory history, CancellationToken token = default)
    {
        var resultFiles = Files(Results).Where(p => Path.GetExtension(p) == ".json").ToArray();
        if (resultFiles.Length > 22000 || resultFiles.Any(p => new FileInfo(p).Length > 32 * 1024 * 1024) || resultFiles.Sum(p => new FileInfo(p).Length) > 220 * 1024 * 1024)
            throw new UserDataException("已保存查询超过单次备份范围，请先在数据管理中整理。");
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                async Task Add(string name, byte[] data)
                { await using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open(); await entry.WriteAsync(data, token); }
                await Add("manifest.json", JsonSerializer.SerializeToUtf8Bytes(new { product = "LeXi", version = 1 }, Json.Options));
                var portable = settings.ForModel(LookupModel.Primary(settings)); portable.CodexPath = "";
                await Add("settings.json", JsonSerializer.SerializeToUtf8Bytes(portable, Json.Options));
                await Add("history.json", JsonSerializer.SerializeToUtf8Bytes(history.Entries, Json.Options));
                foreach (var file in resultFiles)
                {
                    token.ThrowIfCancellationRequested();
                    var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (!Allowed(relative)) continue;
                    await Add(relative, await File.ReadAllBytesAsync(file, token));
                }
            }
            token.ThrowIfCancellationRequested(); File.Move(temp, destination, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static Dictionary<string, byte[]> Read(string archive)
    {
        using var zip = Open(archive);
        if (zip.Entries.Count > 22002) throw new UserDataException("备份文件条目过多。");
        var data = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName != "manifest.json" && !Allowed(entry.FullName)) throw new UserDataException("备份含有不支持的文件，未导入。");
            if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000 || entry.Length > 32 * 1024 * 1024 || (total += entry.Length) > 256 * 1024 * 1024)
                throw new UserDataException("备份超过安全读取范围。");
            using var input = entry.Open(); using var bytes = new MemoryStream();
            var buffer = new byte[16384]; int count;
            while ((count = input.Read(buffer)) > 0)
            { if (bytes.Length + count > entry.Length) throw new UserDataException("备份长度不一致。"); bytes.Write(buffer, 0, count); }
            if (!data.TryAdd(entry.FullName, bytes.ToArray())) throw new UserDataException("备份含有重复条目。");
        }
        if (!data.TryGetValue("manifest.json", out var manifest)) throw new UserDataException("缺少 LeXi 备份清单。");
        using (var doc = JsonDocument.Parse(manifest))
            if (doc.RootElement.GetProperty("product").GetString() != "LeXi" || doc.RootElement.GetProperty("version").GetInt32() != 1)
                throw new UserDataException("备份版本不受支持。");
        foreach (var item in data.Where(p => p.Key != "manifest.json"))
        {
            using var json = JsonDocument.Parse(item.Value);
            if (item.Key == "settings.json")
            { var settings = JsonSerializer.Deserialize<AppSettings>(item.Value, Json.Options) ?? throw new UserDataException("设置为空。"); if (ModelCatalog.Validate(settings) is { } error) throw new UserDataException(error); }
            else if (item.Key == "history.json")
            { if ((JsonSerializer.Deserialize<HistoryEntry[]>(item.Value, Json.Options) ?? []).Any(e => e == null || Queries.Validate(e.Query) != null || e.Visits < 0)) throw new UserDataException("历史记录无效。"); }
            else if (item.Key.StartsWith(SavedQueries.DictionaryKind + "/", StringComparison.Ordinal))
            { var saved = JsonSerializer.Deserialize<SavedDictionary>(item.Value, Json.Options); if (saved == null || !SavedQueries.Valid(saved, saved.Query)) throw new UserDataException("保存的词条无效。"); }
            else if (item.Key.StartsWith(SavedQueries.TranslationKind + "/", StringComparison.Ordinal))
            { var saved = JsonSerializer.Deserialize<SavedTranslation>(item.Value, Json.Options); if (saved is not { Entry: not null, Model: not null, Version: 1 }) throw new UserDataException("保存的翻译无效。"); }
        }
        return data;
    }
    // The archive reader's own messages are English; report a damaged or foreign file in Core wording.
    private static ZipArchive Open(string archive)
    {
        try { return ZipFile.OpenRead(archive); }
        catch (InvalidDataException) { throw new UserDataException("所选文件不是有效的 LeXi 备份。"); }
    }
    public BackupPreview Inspect(string archive)
    {
        var data = Read(archive);
        return new(1, data.TryGetValue("history.json", out var bytes) ? (JsonSerializer.Deserialize<HistoryEntry[]>(bytes, Json.Options) ?? []).Length : 0,
            data.Count(p => p.Key.Contains('/')), data.ContainsKey("settings.json"));
    }
    public BackupPreview Import(string archive, AppSettings settings, QueryHistory history, bool includeSettings)
    {
        var data = Read(archive); // Validate every entry before mutating anything.
        var staged = new Dictionary<string, byte[]>();
        var preview = new BackupPreview(1, 0, data.Count(p => p.Key.Contains('/')), data.ContainsKey("settings.json"));
        if (data.TryGetValue("history.json", out var bytes))
        {
            var imported = JsonSerializer.Deserialize<HistoryEntry[]>(bytes, Json.Options) ?? [];
            preview = preview with { History = imported.Length };
            var combined = history.Entries.Concat(imported).GroupBy(e => (e.Query, e.Translation))
                .Select(g => g.OrderByDescending(e => e.QueriedAt).First() with { Visits = g.Max(e => e.Visits) })
                .OrderByDescending(e => e.QueriedAt).Take(QueryHistory.Capacity).ToArray();
            staged["history.json"] = JsonSerializer.SerializeToUtf8Bytes(combined, Json.Options);
        }
        AppSettings? restored = null;
        if (includeSettings && data.TryGetValue("settings.json", out var config))
        { restored = JsonSerializer.Deserialize<AppSettings>(config, Json.Options)!; restored.CodexPath = settings.CodexPath; SpeechCatalog.AdoptLocal(restored); staged["settings.json"] = JsonSerializer.SerializeToUtf8Bytes(restored, Json.Options); }
        foreach (var item in data.Where(p => p.Key.Contains('/')))
            if (!File.Exists(Path.Combine(root, item.Key.Replace('/', Path.DirectorySeparatorChar)))) staged[item.Key] = item.Value;
        foreach (var name in staged.Keys)
        {
            if (name.Contains('/')) _ = Folder(name.Split('/')[0]);
            var target = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(target) || File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                throw new UserDataException("导入目标被占用或为链接。");
        }
        var rollback = new Dictionary<string, byte[]?>();
        try
        {
            foreach (var item in staged)
            {
                var file = Path.Combine(root, item.Key.Replace('/', Path.DirectorySeparatorChar));
                if (item.Key.Contains('/')) _ = Folder(item.Key.Split('/')[0]);
                if (File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new UserDataException("不能覆盖链接文件。");
                rollback[file] = File.Exists(file) ? File.ReadAllBytes(file) : null;
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                Atomic(file, item.Value);
            }
        }
        catch
        {
            foreach (var item in rollback.Reverse()) { if (item.Value == null) { if (File.Exists(item.Key)) File.Delete(item.Key); } else Atomic(item.Key, item.Value); }
            throw;
        }
        if (restored != null) foreach (var p in typeof(AppSettings).GetProperties().Where(p => p.CanRead && p.CanWrite)) p.SetValue(settings, p.GetValue(restored));
        if (data.TryGetValue("history.json", out bytes)) history.Merge(JsonSerializer.Deserialize<HistoryEntry[]>(bytes, Json.Options) ?? []);
        return preview;
    }
    private static void Atomic(string file, byte[] bytes)
    {
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, file, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
