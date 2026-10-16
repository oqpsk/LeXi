using System.Text;

namespace Lexi.Core;

public sealed record InstalledDictionary(string Name, string Languages, int EntryCount, long Bytes,
    string Folder, string SourceUrl, string License, string Status, bool Available);

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
                bytes = new FileInfo(index).Length + new FileInfo(data).Length;
                available = true; status = "已安装";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            { status = "词库无法读取"; count = 0; }
        }
        else if (File.Exists(index) || File.Exists(data)) status = "词库文件不完整";
        return new("ECDICT 英汉词典", "英汉释义 · 部分词条含英英释义", count, bytes, fullPath,
            "https://github.com/skywind3000/ECDICT", "MIT", status, available);
    }
}
