using System.Net;
using System.Text;
using System.Text.Json;
using Lexi.Core;

internal static class CompatibleChecks
{
    public static async Task Run(string assets, string root)
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
        var credentials = new CompatibleCredentials("synthetic-id", "synthetic-secret");
        const string endpoint = "https://example.org/v1";
        Check(CompatibleText.Url(endpoint + "/", "models").AbsoluteUri == endpoint + "/models" &&
            CompatibleText.Url("https://example.org", "models").AbsolutePath == "/models" &&
            CompatibleText.Url("https://example.org/proxy/v1", "chat/completions").AbsolutePath == "/proxy/v1/chat/completions",
            "text URLs append resource paths without adding or duplicating v1");
        foreach (var invalid in new[] { "http://example.org/v1", "https://user:secret@example.org/v1", "https://example.org/v1?secret=value", "https://example.org/v1#secret" })
        {
            try { CompatibleText.NormalizeBase(invalid); Check(false, "unsafe endpoint"); }
            catch (CompatibleException e) { Check(e.Code == "invalid_endpoint" && !e.Message.Contains(invalid), "endpoint validation rejects plaintext transport and credentials in URLs without echoing them"); }
        }
        Check(JsonSerializer.Serialize(credentials, Lexi.Core.Json.Flexible) == "{}" && !credentials.ToString().Contains("synthetic"),
            "credential objects redact descriptions and serialize no values through normal settings JSON");
        int requests = 0;
        using (var client = new CompatibleTextClient(new Handler(async (request, token) =>
        {
            requests++;
            Check(request.Headers.GetValues("CF-Access-Client-Id").Single() == credentials.ClientId &&
                request.Headers.GetValues("CF-Access-Client-Secret").Single() == credentials.ClientSecret && request.Headers.UserAgent.ToString() == CompatibleText.UserAgent,
                "every text request carries both explicit Access headers and the versioned LeXi User-Agent");
            Check(request.Headers.Authorization == null, "an empty optional API key sends no Bearer authorization header");
            if (request.Method == HttpMethod.Get)
            {
                Check(request.RequestUri!.AbsolutePath == "/v1/models", "model discovery uses GET on the configured base URL");
                return Json("{\"data\":[{\"id\":\"model-a\"},{\"id\":\"vendor/model-b\"},{\"id\":\"model-a\"}]}");
            }
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Check(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/v1/chat/completions" &&
                body.RootElement.GetProperty("model").GetString() == "model-a" && body.RootElement.GetProperty("messages")[0].GetProperty("role").GetString() == "system" &&
                !body.RootElement.TryGetProperty("tools", out _), "text generation posts only the selected model and text messages to Chat Completions");
            Check(body.RootElement.GetProperty("stream").GetBoolean() == (requests == 3), "ordinary and streaming generation use the configured stream flag");
            return Json("{\"choices\":[{\"index\":0,\"finish_reason\":\"stop\",\"message\":{\"content\":\"ordinary JSON\"}}]}");
        })))
        {
            var models = await client.ModelsAsync(endpoint, credentials, default);
            Check(models.SequenceEqual(new[] { "model-a", "vendor/model-b" }), "models are read from data.id with stable case-preserving deduplication");
            Check(await client.CompleteAsync(endpoint, "model-a", false, credentials, "system", "text", default) == "ordinary JSON", "ordinary JSON chat responses are readable");
            Check(await client.CompleteAsync(endpoint, "model-a", true, credentials, "system", "text", default) == "ordinary JSON" && requests == 3,
                "a server's complete JSON response to a streaming request needs no retry or protocol downgrade");
            foreach (var missing in new[] { new CompatibleCredentials(), new("id", ""), new("", "secret"), new("bad\r\nheader", "secret") })
            {
                try { await client.ModelsAsync(endpoint, missing, default); Check(false, "missing Access credential"); }
                catch (CompatibleException e) { Check(e.Code is "missing_credentials" or "invalid_credentials" && requests == 3,
                    "missing or malformed Access credentials fail before any network request"); }
            }
        }
        using (var client = new CompatibleTextClient(new Handler((request, _) =>
        {
            Check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == "synthetic-key" && request.Headers.Contains("CF-Access-Client-Secret"),
                "an explicit optional API key augments rather than replaces the required Access headers");
            return Task.FromResult(Json("{\"data\":[]}"));
        }))) { await client.ModelsAsync(endpoint, new("id", "secret", "synthetic-key"), default); }

        static string Event(string text) => "data: " + JsonSerializer.Serialize(new { choices = new[] { new { index = 0, delta = new { content = text }, finish_reason = (string?)null } } }) + "\n\n";
        const string end = "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
        var previews = new List<string>();
        var gated = new GatedStream(": keepalive\r\nevent: message\r\ndata: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"您\",\"reasoning_content\":\"private reasoning\"},\"finish_reason\":null}]}\r\n\r\n",
            "data: {\"choices\":\ndata: [{\"index\":0,\"delta\":{\"content\":\"好\"},\"finish_reason\":null}]}\n\ndata: {\"choices\":[],\"usage\":{}}\n\n" + end);
        using (var client = new CompatibleTextClient(new Handler((_, _) => Task.FromResult(Events(gated)))))
        {
            var generating = client.CompleteAsync(endpoint, "model-a", true, credentials, "system", "text", default, previews.Add);
            await gated.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(previews.SequenceEqual(new[] { "您" }) && !generating.IsCompleted, "SSE displays the first content delta before the response finishes, excluding reasoning");
            gated.Release.SetResult();
            Check(await generating.WaitAsync(TimeSpan.FromSeconds(5)) == "您好" && previews.SequenceEqual(new[] { "您", "您好" }) && gated.Disposed,
                "SSE handles split UTF-8 bytes, CRLF, comments, multiline data, usage-only chunks and DONE, then closes the stream");
        }
        using (var client = new CompatibleTextClient(new Handler((_, _) => Task.FromResult(Events(new MemoryStream(Encoding.UTF8.GetBytes((Event("CR") + end).Replace('\n', '\r'))))))))
            Check(await client.CompleteAsync(endpoint, "model-a", true, credentials, "system", "text", default) == "CR", "SSE also accepts carriage-return-only event line endings");
        foreach (var malformed in new[] { "[]", "null", "{\"data\":[null]}" })
        {
            using var client = new CompatibleTextClient(new Handler((_, _) => Task.FromResult(Json(malformed))));
            try { await client.ModelsAsync(endpoint, credentials, default); Check(false, "malformed model list"); }
            catch (CompatibleException e) { Check(e.Code == "invalid_response", "malformed model lists return a safe protocol failure rather than a UI exception"); }
        }
        var cancellationStream = new GatedStream(Event("first"), end);
        using (var cancel = new CancellationTokenSource())
        using (var client = new CompatibleTextClient(new Handler((_, _) => Task.FromResult(Events(cancellationStream)))))
        {
            var generating = client.CompleteAsync(endpoint, "model-a", true, credentials, "system", "text", cancel.Token);
            await cancellationStream.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancel.Cancel();
            try { await generating.WaitAsync(TimeSpan.FromSeconds(5)); Check(false, "SSE cancellation"); }
            catch (OperationCanceledException) { Check(cancellationStream.ReadCanceled && cancellationStream.Disposed,
                "canceling generation interrupts the active response read and disposes its network stream"); }
        }
        using (var cancel = new CancellationTokenSource())
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); bool canceled = false;
            using var client = new CompatibleTextClient(new Handler(async (_, token) =>
            { entered.SetResult(); try { await Task.Delay(Timeout.Infinite, token); } catch (OperationCanceledException) { canceled = true; throw; } return Json("{}"); }));
            var generating = client.ModelsAsync(endpoint, credentials, cancel.Token); await entered.Task; cancel.Cancel();
            try { await generating; Check(false, "model discovery cancellation"); }
            catch (OperationCanceledException) { Check(canceled, "canceling model discovery also reaches the HTTP request before headers arrive"); }
        }
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.Found })
        {
            int attempts = 0;
            using var client = new CompatibleTextClient(new Handler((_, _) =>
            { attempts++; return Task.FromResult(new HttpResponseMessage(status) { ReasonPhrase = "synthetic-secret", Content = new StringContent("synthetic-id synthetic-secret private body") }); }));
            try { await client.ModelsAsync(endpoint, credentials, default); Check(false, "Access rejection"); }
            catch (CompatibleException e) { Check(e.Code == "authentication_failed" && !e.Message.Contains("synthetic") && attempts == 1,
                $"HTTP {(int)status} reports authentication failure without echoing private bodies, retrying or dropping authentication"); }
        }
        foreach (var broken in new[] { Event("partial"), Event("partial") + "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"length\"}]}\n\n",
            "data: {bad private payload}\n\n", "data: {\"error\":{\"message\":\"synthetic-secret\"}}\n\n" })
        {
            using var client = new CompatibleTextClient(new Handler((_, _) => Task.FromResult(Events(new MemoryStream(Encoding.UTF8.GetBytes(broken))))));
            try { await client.CompleteAsync(endpoint, "model-a", true, credentials, "system", "text", default); Check(false, "broken stream"); }
            catch (CompatibleException e) { Check(e.Code is "invalid_response" or "incomplete_response" && !e.Message.Contains("private") && !e.Message.Contains("synthetic"),
                "truncated, length-limited and malformed SSE remain explicit failures with sanitized messages"); }
        }
        var settings = new AppSettings { Provider = CompatibleText.Provider, Model = "model-a", ParallelLookup = false, MergeSenses = false, CompatibleBaseUrl = endpoint };
        var alternate = settings.ForModel(LookupModel.Primary(settings)); alternate.CompatibleBaseUrl = "https://other.example.org/v1";
        Check(LookupModel.Primary(settings).CacheKey("word", settings) != LookupModel.Primary(alternate).CacheKey("word", alternate) &&
            Translation.Key("Text.", "zh", settings) != Translation.Key("Text.", "zh", alternate), "dictionary and translation model caches separate identical model IDs on different endpoints");
        Check(ModelCatalog.Validate(settings) == null && ModelCatalog.Choices(settings, ["new-model", "model-a"]).Any(m => m.Provider == "compatible" && m.Id == "new-model") &&
            ModelCatalog.JudgeChoices(ModelCatalog.Choices(settings)).Any(m => m.Provider == "compatible"), "discovered text models can be selected for lookup, translation and explicit sense judging");
        await InferenceChecks.Run(root, Check);
        await SparseMergeChecks.Transport(assets, root, Check);
        await CheckDictionary(assets, root, settings, credentials, Check);
        Console.WriteLine($"{count} custom text provider checks passed");
    }
    private static async Task CheckDictionary(string assets, string root, AppSettings settings, CompatibleCredentials credentials, Action<bool, string> check)
    {
        var entry = new AiEntry("synthetic", true, [new("noun", "A synthetic concept.", "合成概念", "", "A synthetic example.", "一个合成例句。", [], ChineseTerm: "合成概念", CoreMeaning: true)], "", "unverified", WordForms: [], Etymology: "这是测试词条的语源说明。");
        var json = JsonSerializer.Serialize(entry, Lexi.Core.Json.Flexible);
        using var client = new CompatibleTextClient(new Handler((_, _) => Task.FromResult(Json(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = json }, finish_reason = "stop" } } })))));
        var dictionary = new CompatibleDictionary(assets, client, _ => credentials, root);
        var previews = new List<AiEntry>();
        var result = await dictionary.LookupAsync("synthetic", null, settings, default, new ProgressInline<AiEntry>(previews.Add));
        check(result.Senses[0].Chinese == "合成概念" && result.EvidenceStatus == "unverified" && previews.Count > 0,
            "custom dictionary output uses the shared schema, preview parser and provenance validation");
        var cache = new Cache(Path.Combine(root, Guid.NewGuid().ToString("N")));
        var bad = new DictionaryCoordinator(cache, (_, _, _, _, _, _) => throw new CompatibleException("invalid_response", "Synthetic interrupted stream"),
            (_, _, _) => throw new Exception("Judge should not run"));
        var snapshot = await bad.LookupAsync("synthetic", null, settings, false, default);
        check(snapshot.Answers.Single().Phase == ModelPhase.Failed && cache.Read<AiEnvelope>(DictionaryCoordinator.CacheKind, LookupModel.Primary(settings).CacheKey("synthetic", settings)) == null,
            "interrupted custom generation cannot enter the completed dictionary cache");
    }
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Events(Stream stream)
    { var content = new StreamContent(stream); content.Headers.ContentType = new("text/event-stream"); return new(HttpStatusCode.OK) { Content = content }; }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => run(request, token); }
    private sealed class ProgressInline<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
    private sealed class GatedStream(string first, string second) : Stream
    {
        private readonly byte[] a = Encoding.UTF8.GetBytes(first), b = Encoding.UTF8.GetBytes(second);
        private int position; public bool Disposed, ReadCanceled;
        public readonly TaskCompletionSource Blocked = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (position >= a.Length && !Release.Task.IsCompleted)
            {
                Blocked.TrySetResult();
                try { await Release.Task.WaitAsync(cancellationToken); } catch (OperationCanceledException) { ReadCanceled = true; throw; }
            }
            if (position >= a.Length + b.Length) return 0;
            buffer.Span[0] = position < a.Length ? a[position] : b[position - a.Length]; position++; return 1;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => a.Length + b.Length; public override long Position { get => position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
