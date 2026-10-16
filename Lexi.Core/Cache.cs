using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lexi.Core;

public sealed class Cache(string root)
{
    private string PathFor(string kind, string query) => Path.Combine(root, kind, Hash(query) + ".json");
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public T? Read<T>(string kind, string query)
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(PathFor(kind, query)), Json.Options); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return default; }
    }
    public async Task WriteAsync<T>(string kind, string query, T value)
    {
        var path = PathFor(kind, query);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, Json.Options)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
