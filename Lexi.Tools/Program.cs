using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using Lexi.Core;

if (args.Length < 1) throw new ArgumentException("build-data <csv> <folder> | check <folder> [--fixture] | stream-check | live <folder> <assets> <cache> <word> [model] | bench-claude <assets> <work-folder> <words...> | cancel-check <assets> <work-folder>");
if (args[0] == "build-data")
{
    Directory.CreateDirectory(args[2]);
    var entries = new Dictionary<string, LocalEntry>(StringComparer.Ordinal);
    using var csv = new TextFieldParser(args[1], Encoding.UTF8) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
    csv.SetDelimiters(","); csv.ReadFields();
    while (!csv.EndOfData)
    {
        var f = csv.ReadFields()!;
        if (f.Length < 11) continue;
        var key = Queries.Normalize(f[0]);
        if (key.Length == 0) continue;
        int.TryParse(f[9], out var freq);
        if (freq <= 0) int.TryParse(f[8], out freq);
        var entry = new LocalEntry(f[0], f[1], f[2].Replace("\\n", "\n"), f[3].Replace("\\n", "\n"), f[10], freq);
        if (!entries.TryGetValue(key, out var existing) || entry.English.Length + entry.Chinese.Length > existing.English.Length + existing.Chinese.Length) entries[key] = entry;
    }
    using var data = File.Create(Path.Combine(args[2], "lexicon.dat"));
    using var index = new BinaryWriter(File.Create(Path.Combine(args[2], "lexicon.idx")), Encoding.UTF8);
    index.Write("LEXI1"); index.Write(entries.Count);
    var forms = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var (key, entry) in entries.OrderBy(x => x.Key, StringComparer.Ordinal))
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(entry, Json.Options);
        index.Write(key); index.Write(data.Position); index.Write(bytes.Length); index.Write(entry.Frequency); data.Write(bytes);
        foreach (var item in entry.Exchange.Split('/'))
        {
            var pair = item.Split(':', 2);
            if (pair.Length == 2 && new[] { "p", "d", "i", "3", "r", "t", "s" }.Contains(pair[0]))
                foreach (var form in pair[1].Split(',')) forms.TryAdd(Queries.Normalize(form), key);
        }
    }
    index.Write(forms.Count);
    foreach (var (form, lemma) in forms) { index.Write(form); index.Write(lemma); }
    Console.WriteLine($"Built {entries.Count:N0} entries and {forms.Count:N0} inflected forms.");
}
else if (args[0] == "check")
{
    int count = 0;
    void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
    var sw = Stopwatch.StartNew();
    using var dict = new OfflineDictionary(args[1]);
    Console.WriteLine($"Loaded {dict.Count:N0} entries in {sw.ElapsedMilliseconds} ms");
    var isFixture = args.Contains("--fixture");
    Check(isFixture ? dict.Count >= 3 : dict.Count > 700000, isFixture ? "synthetic offline fixture" : "full offline corpus");
    Check(dict.Lookup("hello") is { English.Length: > 0, Chinese.Length: > 0 }, "offline bilingual entry");
    Check(dict.Lookup("  HELLO  ")?.Word == "hello", "case and whitespace normalization");
    Check(dict.Lookup("look up")?.Chinese.Length > 0, "multiword phrases");
    Check(dict.Lookup("serendipity")?.English.Contains("discover", StringComparison.OrdinalIgnoreCase) == true, "English definition");
    Check(dict.Lookup("zqxjkvzzzz123") == null, "unknown word stays missing");
    Check(dict.Suggest("serend").Contains("serendipity"), "prefix suggestions");
    // ECDICT contains a separate "walked" entry, which takes precedence over the lemma.
    Check(dict.Lookup("walked")?.Word == (isFixture ? "walk" : "walked"), isFixture ? "inflected word lookup" : "exact entry takes precedence over inflection");
    Check(Queries.Normalize("ＨＥＬＬＯ  world") == "hello world", "Unicode normalization");
    Check(Queries.Validate(new string('a', 101)) != null, "query length bound");
    var fixture = "{\"en\":[{\"language\":\"Translingual\",\"partOfSpeech\":\"Symbol\",\"definitions\":[{\"definition\":\"ISO code\"}]},{\"language\":\"English\",\"partOfSpeech\":\"Verb\",\"definitions\":[{\"definition\":\"To &amp; <b>outclass</b>.<script>bad()</script>\"}]}]}";
    var online = OnlineDictionary.Parse("mog", fixture)!;
    Check(online.Senses.Length == 1 && online.Senses[0].Definition == "To & outclass.", "online language filtering and HTML safety");
    var ai = new AiEntry("mog", true, [new("verb", "Outclass", "胜过", "slang", "", "", [1])], "", "source_based");
    Check(CodexDictionary.ParseAndValidate(JsonSerializer.Serialize(ai, Json.Options), "mog", online).EvidenceStatus == "source_based", "valid numbered provenance");
    var ungrounded = ai with { Senses = [ai.Senses[0] with { Evidence = [] }] };
    Check(CodexDictionary.ParseAndValidate(JsonSerializer.Serialize(ungrounded, Json.Options), "mog", online).EvidenceStatus == "unverified", "ungrounded AI cannot be labeled source-based");
    try { CodexDictionary.ParseAndValidate(JsonSerializer.Serialize(ai, Json.Options), "hello", online); Check(false, "wrong query rejected"); }
    catch (InvalidDataException) { Check(true, "wrong query rejected"); }
    try { CodexDictionary.ParseAndValidate(JsonSerializer.Serialize(ai with { Senses = [ai.Senses[0] with { Evidence = [99] }] }, Json.Options), "mog", online); Check(false, "bad citations rejected"); }
    catch (InvalidDataException) { Check(true, "bad citations rejected"); }
    sw.Restart(); for (int i = 0; i < 1000; i++) dict.Lookup("serendipity");
    Console.WriteLine($"1000 offline queries: {sw.ElapsedMilliseconds} ms; {count} checks passed");
}
else if (args[0] == "stream-check")
{
    int count = 0;
    void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
    var original = new AiEntry("mog", true, [new("verb", "outshine \"someone\" {figuratively}", "胜过；压过风头", "slang", "He mogs.", "他更出众。", [1])], "", "source_based");
    var json = JsonSerializer.Serialize(original, Json.Options);
    var preview = StreamingEntry.Read(json, "mog");
    Check(preview?.Senses[0].Chinese == original.Senses[0].Chinese && preview.Senses[0].English == original.Senses[0].English, "escaped strings and Unicode survive streaming");
    Check(preview?.EvidenceStatus == "unverified" && preview.Senses.All(s => s.Evidence.Length == 0), "unfinished result never claims verified citations");
    Check(StreamingEntry.Read(json, "hello") == null, "other query cannot leak into current preview");
    Check(StreamingEntry.Read("```json\n" + json + "\n```", "mog")?.Senses.Length == 1, "markdown fence tolerance");
    Check(StreamingEntry.Read("{\"word\":\"mog\",\"senses\":[{\"english\":\"outshine\",\"chinese\":\"incomplete", "mog") == null, "incomplete bilingual pair stays hidden");
    var partial = "{\"word\":\"mog\",\"senses\":[{\"chinese\":\"胜过\",\"english\":\"outshine\",\"example\":\"unfinished";
    Check(StreamingEntry.Read(partial,"mog")?.Senses[0].Chinese == "胜过", "definitions appear before examples finish");
    for (var i = 0; i < json.Length; i++) _ = StreamingEntry.Read(json[..i], "mog");
    Check(true, "every possible character boundary parses safely");
    Console.WriteLine($"{count} stream checks passed");
}
else if (args[0] == "cancel-check")
{
    using var claude = new ClaudeDictionary(args[1], args[2]);
    using var cancel = new CancellationTokenSource(100);
    try { await claude.LookupAsync("mog", null, "haiku", cancel.Token); throw new Exception("Cancellation did not stop the query"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS: active query cancellation"); }
    var result = await claude.LookupAsync("hello", null, "haiku", CancellationToken.None);
    if (result.Word != "hello" || !result.Found || result.Senses.Length == 0) throw new Exception("Recovery query failed");
    Console.WriteLine("PASS: next query recovers with an independent result");
}
else if (args[0] == "bench-claude")
{
    using var web = new OnlineDictionary();
    using var claude = new ClaudeDictionary(args[1], args[2]);
    claude.Prepare("haiku");
    var rows = new List<object>();
    foreach (var query in args.Skip(3))
    {
        var sw = Stopwatch.StartNew(); long? first = null;
        var online = await web.LookupAsync(query, CancellationToken.None);
        var onlineMs = sw.ElapsedMilliseconds;
        var ai = await claude.LookupAsync(query, online, "haiku", CancellationToken.None, new CallbackProgress<AiEntry>(_ => first ??= sw.ElapsedMilliseconds));
        var row = new { query, onlineMs, firstDefinitionMs = first, totalMs = sw.ElapsedMilliseconds, entry = ai };
        rows.Add(row); Console.WriteLine(JsonSerializer.Serialize(row,Json.Options));
    }
    Directory.CreateDirectory(args[2]);
    File.WriteAllText(Path.Combine(args[2], "benchmark.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions(Json.Options) { WriteIndented = true }));
}
else if (args[0] == "live")
{
    using var dict = new OfflineDictionary(args[1]);
    using var web = new OnlineDictionary();
    var query = Queries.Normalize(args[4]);
    var sw = Stopwatch.StartNew();
    var entry = await web.LookupAsync(query, CancellationToken.None);
    Console.WriteLine($"Online: {entry?.Senses.Length ?? 0} senses in {sw.ElapsedMilliseconds} ms");
    var cache = new Cache(args[3]);
    if (entry != null) await cache.WriteAsync("online", query, entry);
    var settings = new AppSettings { Model = args.Length > 5 ? args[5] : "" };
    var ai = await new CodexDictionary(args[2], args[3]).LookupAsync(query, entry, settings, CancellationToken.None);
    await cache.WriteAsync("ai", query, new AiEnvelope(ai, CodexDictionary.EvidenceHash(entry), DateTimeOffset.UtcNow, settings.Model, settings.ReasoningEffort));
    Console.WriteLine(JsonSerializer.Serialize(ai, new JsonSerializerOptions(Json.Options) { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    Console.WriteLine($"Total: {sw.ElapsedMilliseconds} ms");
}

else throw new ArgumentException("Unknown command: " + args[0]);

sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T> { public void Report(T value) => callback(value); }
