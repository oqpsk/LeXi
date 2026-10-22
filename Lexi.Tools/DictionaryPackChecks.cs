using System.IO.Compression;
using System.Security.Cryptography;
using Lexi.Core;

// Installing a downloaded dictionary pack (iOS): the archive must be the pinned one, and installing or
// removing changes the loaded dictionaries without a restart.
internal static class DictionaryPackChecks
{
    public static async Task Run(string fixture, string root)
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }
        var work = Path.Combine(Path.GetFullPath(root), "dictionary-packs");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        var data = Path.Combine(work, "data");
        Directory.CreateDirectory(data);
        var license = Path.Combine(work, "LICENSE.txt");
        File.WriteAllText(license, "test licence");

        // The fixture dictionary in the shape of the ECDICT pack, pinned to its own size and hash.
        var shape = DictionaryPacks.Find("ecdict")!;
        var archive = Path.Combine(work, "pack.zip");
        DictionaryPacks.Write(Path.GetFullPath(fixture), license, shape, archive);
        long unpacked;
        using (var zip = ZipFile.OpenRead(archive)) unpacked = zip.Entries.Sum(e => e.Length);
        var pack = shape with { Bytes = new FileInfo(archive).Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archive))), InstalledBytes = unpacked };

        using var session = new QuerySession(new LexiPaths(Path.Combine(work, "assets"), data, Path.Combine(work, "user")));
        await session.InitializeAsync();
        Check((await session.Library).Count == 0 && session.Suggest("hel").Length == 0, "a new install starts without an offline dictionary");
        var damaged = Path.Combine(work, "damaged.zip");
        var bytes = File.ReadAllBytes(archive);
        bytes[^100] ^= 0xFF;
        File.WriteAllBytes(damaged, bytes);
        var refused = false;
        try { await session.InstallDictionaryAsync(pack, damaged); }
        catch (DictionaryInstallException) { refused = true; }
        Check(refused && !Directory.EnumerateFileSystemEntries(data).Any(), "a damaged download is refused and leaves nothing behind");
        await session.InstallDictionaryAsync(pack, archive);
        Check((await session.Library).Count > 0 && session.Suggest("hel").Contains("hello") && File.Exists(Path.Combine(data, shape.LicenseFile)) &&
            !Directory.Exists(Path.Combine(data, ".install-ecdict")), "a verified pack installs at the data root with its licence and is used without restarting");
        await session.RemoveDictionaryAsync("ecdict");
        Check((await session.Library).Dictionaries.Length == 0 && !Directory.EnumerateFileSystemEntries(data).Any() && session.Suggest("hel").Length == 0,
            "removing the dictionary deletes its files and unloads it");
        Console.WriteLine($"{count} dictionary pack checks passed");
    }
}
