namespace Lexi.Core;

public sealed partial class QuerySession
{
    // Installs a downloaded pack (DictionaryPacks) while no query runs, then reloads the dictionaries.
    public Task InstallDictionaryAsync(string id, string archive, CancellationToken token = default) =>
        InstallDictionaryAsync(DictionaryPacks.Find(id) ?? throw new DictionaryInstallException("没有这部词典。"), archive, token);
    internal Task InstallDictionaryAsync(DictionaryPack pack, string archive, CancellationToken token = default) => ManageDataAsync(async () =>
    {
        var staging = Path.Combine(Paths.DataRoot, ".install-" + pack.Id);
        try
        {
            await Task.Run(() => DictionaryPacks.Unpack(pack, archive, staging, token), token);
            await ReplaceDictionariesAsync(() =>
            {
                foreach (var file in pack.Files) File.Move(Path.Combine(staging, file), Path.Combine(Paths.DataRoot, file), true);
            });
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        return true;
    });

    // Deletes an installed dictionary: the files at the data root (ECDICT), or a dictionary's own folder.
    public Task RemoveDictionaryAsync(string id) => ManageDataAsync(async () =>
    {
        var root = Path.GetFullPath(Paths.DataRoot);
        var installed = (await Library).Dictionaries.FirstOrDefault(d => d.Id == id) ?? throw new DictionaryInstallException("没有这部词典。");
        var folder = Path.GetFullPath(installed.Folder);
        await ReplaceDictionariesAsync(() =>
        {
            if (folder == root)
                foreach (var file in new[] { "lexicon.idx", "lexicon.dat" }.Concat(DictionaryPacks.All.Select(p => p.LicenseFile)))
                    File.Delete(Path.Combine(root, file));
            else if (folder.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) Directory.Delete(folder, true);
        });
        return true;
    });

    // Changes the dictionary files and loads them again. Windows cannot replace or delete a file that
    // is open, so the loaded dictionaries are closed first there; elsewhere a lookup still reading the
    // old files keeps them until it ends.
    private async Task ReplaceDictionariesAsync(Action change)
    {
        var current = await Library;
        if (OperatingSystem.IsWindows())
            lock (gate) { current.Dispose(); foreach (var library in retiredLibraries) library.Dispose(); retiredLibraries.Clear(); }
        try { change(); }
        finally
        {
            var reloaded = await Task.Run(() => new OfflineLibrary(Paths.DataRoot));
            lock (gate)
            {
                if (!OperatingSystem.IsWindows()) retiredLibraries.Add(current);
                Library = Task.FromResult(reloaded);
                Publish();
            }
        }
    }
}
