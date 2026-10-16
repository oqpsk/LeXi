using System.Text;
using System.Text.Json;

namespace Lexi.Core;

public sealed record InstalledDictionary(string Name, string Languages, int EntryCount, long Bytes,
    string Folder, string SourceUrl, string License, string Status, bool Available,
    string Id = "ecdict", string Version = "", string Attribution = "", bool AllowAiEvidence = true);

public sealed record DictionaryMetadata(string Id, string Name, string Languages, string SourceUrl,
    string License, string Version, string Attribution, bool AllowAiEvidence = true);

public static class DictionaryCatalog
{
    public static InstalledDictionary Inspect(string folder)
    {
        var fullPath = Path.GetFullPath(folder);
        var index = Path.Combine(fullPath, "lexicon.idx");
        var data = Path.Combine(fullPath, "lexicon.dat");
        var status = "未安装";
        int count = 0; long bytes = 0; bool available = false;
        if (File.Exists(index) && File.Exists(data))
        {
            try
            {
                using var reader = new BinaryReader(File.OpenRead(index), Encoding.UTF8);
                if (reader.ReadString() != "LEXI1") throw new InvalidDataException();
                count = reader.ReadInt32();
                if (count <= 0 || new FileInfo(data).Length == 0) throw new InvalidDataException();
                bytes = Directory.EnumerateFiles(fullPath).Sum(file => new FileInfo(file).Length);
                available = true; status = "已安装";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            { status = "词库无法读取"; count = 0; }
        }
        else if (File.Exists(index) || File.Exists(data)) status = "词库文件不完整";
        var metadata = new DictionaryMetadata("ecdict", "ECDICT 英汉词典", "英汉释义 · 部分词条含英英释义",
            "https://github.com/skywind3000/ECDICT", "MIT", "", "skywind3000 / ECDICT");
        if (File.Exists(Path.Combine(folder, "dictionary.json")))
        {
            try { metadata = JsonSerializer.Deserialize<DictionaryMetadata>(File.ReadAllText(Path.Combine(folder, "dictionary.json")), Json.Options) ?? throw new InvalidDataException(); }
            catch (Exception e) when (e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
            { status = "词典信息损坏"; available = false; }
        }
        return new(metadata.Name, metadata.Languages, count, bytes, fullPath, metadata.SourceUrl, metadata.License,
            status, available, metadata.Id, metadata.Version, metadata.Attribution, metadata.AllowAiEvidence);
    }
    public static InstalledDictionary[] InspectAll(string root)
    {
        var result = new List<InstalledDictionary>();
        if (File.Exists(Path.Combine(root, "lexicon.idx")) || File.Exists(Path.Combine(root, "lexicon.dat"))) result.Add(Inspect(root));
        if (Directory.Exists(root)) foreach (var folder in Directory.EnumerateDirectories(root).Order())
            if (!Path.GetFileName(folder).StartsWith('.') && File.Exists(Path.Combine(folder, "dictionary.json"))) result.Add(Inspect(folder));
        return result.ToArray();
    }
}
