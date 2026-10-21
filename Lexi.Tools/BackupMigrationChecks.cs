using System.IO.Compression;
using System.Text.Json;
using Lexi.Core;

internal static class BackupMigrationChecks
{
    public static async Task Run(string root)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
        var work = Path.Combine(Path.GetFullPath(root), "migration-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(work, "source");
        var history = new QueryHistory(source); history.Record("ABS", false); history.Record("abs", false);
        var model = new LookupModel("openai", "gpt-6-luna");
        var cache = new Cache(source);
        await cache.WriteAsync(SavedQueries.TranslationKind, "saved translation",
            new SavedTranslation(new(Cache.Hash("Hello"), "zh", ["你好"], ""), model, DateTimeOffset.UtcNow));
        var mobile = new AppSettings { Provider = model.Provider, Model = model.Model, SecondaryProvider = "anthropic",
            SecondaryModel = "claude-haiku-5-5", JudgeProvider = "openai", JudgeModel = "gpt-6-luna",
            Online = false, AutoAi = false, SpeechProvider = "ios", SpeechModel = "avspeech", SpeechVoice = "device-specific" };
        var archive = Path.Combine(work, "mobile.lexibackup");
        await new UserData(source).ExportAsync(archive, mobile, history);
        var target = Path.Combine(work, "target");
        var data = new UserData(target); var targetHistory = new QueryHistory(target);
        var current = new AppSettings { Provider = CompatibleText.Provider, Model = "current-model", SecondaryProvider = "codex",
            SecondaryModel = "current-second", JudgeProvider = CompatibleText.Provider, JudgeModel = "current-judge",
            CompatibleBaseUrl = "https://current.example.org/v1", CodexPath = "local-client", ReasoningEffort = "medium" };
        current.InferenceProfiles = [new(CompatibleText.Provider, "current-model", CompatibleText.Scope(current), "low", "priority")];
        var identity = LookupModel.Primary(current).CacheKey("test", current);
        var preview = data.Inspect(archive);
        Check(preview is { History: 2, Results: 1, HasSettings: true }, "an iOS API-model backup can be inspected on desktop before importing");
        data.Import(archive, current, targetHistory, false);
        var original = Directory.GetFiles(Path.Combine(source, SavedQueries.TranslationKind)).Single();
        var restored = Directory.GetFiles(Path.Combine(target, SavedQueries.TranslationKind)).Single();
        Check(targetHistory.Entries.Any(h => h.Query == "ABS") && targetHistory.Entries.Any(h => h.Query == "abs") &&
            File.ReadAllBytes(original).SequenceEqual(File.ReadAllBytes(restored)) && current.Online && current.AutoAi &&
            LookupModel.Primary(current).CacheKey("test", current) == identity,
            "excluding settings imports case-preserving history and completed results without changing current models or preferences");
        data.Import(archive, current, targetHistory, true);
        var reloaded = AppSettings.Load(target);
        Check(current is { Provider: CompatibleText.Provider, Model: "current-model", SecondaryProvider: "codex", SecondaryModel: "current-second",
                JudgeProvider: CompatibleText.Provider, JudgeModel: "current-judge", CodexPath: "local-client", Online: false, AutoAi: false } &&
            current.CompatibleBaseUrl == "https://current.example.org/v1" && current.InferenceProfiles.Length == 1 &&
            LookupModel.Primary(current).CacheKey("test", current) == identity && reloaded.Model == current.Model &&
            ModelCatalog.Validate(current) == null && current.SpeechProvider == SpeechCatalog.Local.Provider && current.SpeechVoice == "auto",
            "unsupported imported services preserve the complete local model and endpoint configuration while other settings and local speech migrate");

        var desktop = new AppSettings { Provider = "claude", Model = "sonnet", JudgeProvider = "codex", JudgeModel = "gpt-6-luna", Online = false };
        var mobileTarget = new AppSettings { Provider = "openai", Model = "gpt-6.1-sol", SecondaryProvider = "anthropic", SecondaryModel = "claude-sonnet-5-5",
            JudgeProvider = "openai", JudgeModel = "gpt-6-luna" };
        var onMobile = UserData.RestoreSettings(desktop, mobileTarget, true);
        Check(ModelCatalog.ValidatePortable(desktop) == null && onMobile is { Provider: "openai", Model: "gpt-6.1-sol", SecondaryProvider: "anthropic",
            SecondaryModel: "claude-sonnet-5-5", JudgeProvider: "openai", Online: false } && ModelCatalog.Validate(onMobile, true) == null,
            "desktop-to-iOS restoration keeps the device's working API models instead of selecting an unavailable OAuth client");
        var shared = new AppSettings { Provider = CompatibleText.Provider, Model = "shared-first", SecondaryProvider = CompatibleText.Provider,
            SecondaryModel = "shared-second", JudgeProvider = CompatibleText.Provider, JudgeModel = "shared-judge", CompatibleBaseUrl = "https://shared.example.org/v1" };
        shared.InferenceProfiles = [new(CompatibleText.Provider, "shared-first", CompatibleText.Scope(shared), "low", "priority")];
        Check(new[] { false, true }.All(api =>
        {
            var adopted = UserData.RestoreSettings(shared, api ? mobileTarget : current, api);
            return adopted.Model == "shared-first" && adopted.SecondaryModel == "shared-second" && adopted.JudgeModel == "shared-judge" &&
                adopted.CompatibleBaseUrl == shared.CompatibleBaseUrl && adopted.InferenceProfiles.SequenceEqual(shared.InferenceProfiles) &&
                ModelCatalog.Validate(adopted, api) == null;
        }), "compatible endpoints and per-model profiles restore unchanged on desktop and iOS");
        Check(UserData.RestoreSettings(desktop, current, false).Provider == "claude" &&
            UserData.RestoreSettings(mobile, mobileTarget, true).Model == mobile.Model,
            "supported model selections restore rather than being replaced by local defaults");

        string Invalid(AppSettings settings)
        {
            var path = Path.Combine(work, Guid.NewGuid().ToString("N") + ".lexibackup");
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write("{\"product\":\"LeXi\",\"version\":1}");
            using (var writer = new StreamWriter(zip.CreateEntry("settings.json").Open())) writer.Write(JsonSerializer.Serialize(settings, Json.Options));
            return path;
        }
        var badModel = mobile.ForModel(new("openai", "bad model"));
        var badOptions = mobile.ForModel(model); badOptions.InferenceProfiles = [new("codex", "x", "", "invalid", "")];
        var before = File.ReadAllBytes(Path.Combine(target, "history.json"));
        foreach (var invalid in new[] { mobile.ForModel(new("unknown-service", "model")), badModel, badOptions })
        {
            var rejected = false;
            try { data.Import(Invalid(invalid), current, targetHistory, false); }
            catch (UserDataException) { rejected = true; }
            Check(rejected && File.ReadAllBytes(Path.Combine(target, "history.json")).SequenceEqual(before),
                "invalid backup model providers, IDs and inference options are rejected before mutation even with settings excluded");
        }
    }
}
