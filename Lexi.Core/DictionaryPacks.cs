using System.IO.Compression;
using System.Security.Cryptography;

namespace Lexi.Core;

// A dictionary the app downloads in one piece where no install script runs (iOS): a zip of the
// built index and data with the dictionary's licence, published with this repository's releases and
// pinned by size and SHA-256. `Lexi.Tools pack-data` writes it from a built folder; ECDICT is built
// from the revision scripts/Install-Dictionary.ps1 pins. A pack installs at the data root, where
// ECDICT lives on every platform.
public sealed record DictionaryPack(string Id, string Name, string Languages, string Url, long Bytes, string Sha256,
    long InstalledBytes, string License, string LicenseFile)
{
    public string[] Files => ["lexicon.idx", "lexicon.dat", LicenseFile];
}

// The reason a dictionary was not installed or removed, worded for the reader.
public sealed class DictionaryInstallException(string message) : Exception(message);

public static class DictionaryPacks
{
    public static readonly DictionaryPack[] All =
    [
        new("ecdict", "ECDICT 英汉词典", "英汉释义 · 部分词条含英英释义",
            "https://github.com/oqpsk/LeXi/releases/download/dictionary-ecdict-bc015ed/ecdict-bc015ed.zip",
            33_799_519, "d0f044e46381ee222e5f38d36320b33e2a72fd5869b3df50f39e0269a7c57065", 170_990_957, "MIT", "ECDICT-LICENSE.txt"),
    ];
    public static DictionaryPack? Find(string id) => All.FirstOrDefault(p => p.Id == id);

    // Same files, same order and fixed times, so building a pack twice gives the same archive.
    public static void Write(string folder, string license, DictionaryPack pack, string output)
    {
        using var zip = ZipFile.Open(output, ZipArchiveMode.Create);
        foreach (var file in pack.Files)
        {
            var entry = zip.CreateEntry(file, CompressionLevel.SmallestSize);
            entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var source = File.OpenRead(file == pack.LicenseFile ? license : Path.Combine(folder, file));
            using var target = entry.Open();
            source.CopyTo(target);
        }
    }

    // A downloaded archive must be the published one; its files are unpacked into an empty staging
    // folder and the index must open there before anything installed is touched.
    public static void Unpack(DictionaryPack pack, string archive, string staging, CancellationToken token = default)
    {
        using (var stream = File.OpenRead(archive))
        {
            if (stream.Length != pack.Bytes) throw new DictionaryInstallException("下载的词典不完整，请重新下载。");
            if (Convert.ToHexStringLower(SHA256.HashData(stream)) != pack.Sha256) throw new DictionaryInstallException("下载的词典校验失败，请重新下载。");
        }
        token.ThrowIfCancellationRequested();
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        using (var zip = ZipFile.OpenRead(archive))
        {
            if (zip.Entries.Count != pack.Files.Length || zip.Entries.Any(e => !pack.Files.Contains(e.FullName)) || zip.Entries.Sum(e => e.Length) != pack.InstalledBytes)
                throw new DictionaryInstallException("下载的词典内容不符，请重新下载。");
            foreach (var entry in zip.Entries) { token.ThrowIfCancellationRequested(); entry.ExtractToFile(Path.Combine(staging, entry.FullName)); }
        }
        try { using var dictionary = new OfflineDictionary(staging); if (dictionary.Count == 0) throw new InvalidDataException(); }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or IOException)
        { throw new DictionaryInstallException("下载的词典无法读取，请重新下载。"); }
    }
}
