using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Lexi.Core;

namespace Lexi.Host;

public sealed class HostException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

// One JSON object per line in each direction (docs/host-protocol.md). Every request id gets
// exactly one response; "view" events stream each QueryView the session publishes, in order.
// speechClient replaces the online speech transport in offline checks.
public sealed class HostServer(QuerySession session, TextReader input, TextWriter output, SpeechClient? speechClient = null, CompatibleTextClient? textClient = null)
{
    public const int Protocol = 1;
    public const int MaxLine = 1 << 20;
    public static string Version => typeof(HostServer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public static readonly JsonSerializerOptions Options = new(Json.Options)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        // Chinese text stays readable; JSON still escapes every control character, so one message is one line.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly JsonElement NoParameters = JsonDocument.Parse("{}").RootElement.Clone();
    private readonly Channel<string> outgoing = Channel.CreateUnbounded<string>(new() { SingleReader = true });
    private readonly ConcurrentDictionary<string, CancellationTokenSource> running = new();
    private readonly SpeechClient metadata = speechClient ?? new();
    private readonly SpeechAudio speech = new(session.Paths.UserRoot, speechClient);
    private readonly CompatibleTextClient text = textClient ?? new();

    public async Task RunAsync(CancellationToken token = default)
    {
        void Publish(QueryView view) => Send(new { @event = "view", data = view });
        session.Changed += Publish;
        var writer = Task.Run(WriteAsync, CancellationToken.None);
        var handlers = new List<Task>();
        JsonElement? shutdown = null;
        try
        {
            while (await input.ReadLineAsync(token) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.Length > MaxLine) { Fail(null, "invalid_request", "Request exceeds the line limit."); continue; }
                if (!TryParse(line, out var id, out var method, out var parameters)) continue;
                if (method == "shutdown") { shutdown = id; break; }
                handlers.RemoveAll(t => t.IsCompleted);
                handlers.Add(Task.Run(() => HandleAsync(id!.Value, method, parameters), CancellationToken.None));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            // Closing input ends every running request; nothing else is written after shutdown.
            session.Cancel();
            foreach (var request in running.Values) request.Cancel();
            await Task.WhenAll(handlers);
            session.Changed -= Publish;
            speech.Dispose(); if (speechClient == null) metadata.Dispose(); if (textClient == null) text.Dispose();
            if (shutdown != null) Reply(shutdown, new { });
            outgoing.Writer.TryComplete();
            await writer;
        }
    }

    private bool TryParse(string line, out JsonElement? id, out string method, out JsonElement parameters)
    {
        id = null; method = ""; parameters = NoParameters;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
            if (root.TryGetProperty("id", out var value) && value.ValueKind is JsonValueKind.Number or JsonValueKind.String) id = value.Clone();
            if (id == null) { Fail(null, "invalid_request", "Every request needs a string or number id."); return false; }
            if (!root.TryGetProperty("method", out var name) || name.ValueKind != JsonValueKind.String) { Fail(id, "invalid_request", "Missing method."); return false; }
            method = name.GetString()!;
            if (root.TryGetProperty("params", out var supplied) && supplied.ValueKind != JsonValueKind.Null)
            {
                if (supplied.ValueKind != JsonValueKind.Object) { Fail(id, "invalid_params", "params must be an object."); return false; }
                parameters = supplied.Clone();
            }
            return true;
        }
        catch (JsonException) { Fail(null, "parse_error", "The request is not valid JSON."); return false; }
    }

    private async Task HandleAsync(JsonElement id, string method, JsonElement parameters)
    {
        var key = id.GetRawText();
        using var cancel = new CancellationTokenSource();
        if (!running.TryAdd(key, cancel)) { Fail(id, "invalid_request", "A request with this id is still running."); return; }
        try { Reply(id, await DispatchAsync(method, parameters, cancel.Token)); }
        catch (CompatibleException e) { Fail(id, e.Code, e.Message); }
        catch (HostException e) { Fail(id, e.Code, e.Message); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { Fail(id, "cancelled", "Request cancelled."); }
        catch (JsonException) { Fail(id, "invalid_params", "数据格式无效。"); }
        // Unexpected failures report only their category; messages can contain local paths.
        catch (Exception e) { Fail(id, "internal_error", e.GetType().Name); }
        finally { running.TryRemove(key, out _); }
    }

    private async Task<object?> DispatchAsync(string method, JsonElement p, CancellationToken token)
    {
        switch (method)
        {
            case "initialize":
                await session.InitializeAsync();
                return new
                {
                    protocol = Protocol, version = Version,
                    platform = OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsWindows() ? "windows" : "linux",
                    paths = new { data = session.Paths.DataRoot, user = session.Paths.UserRoot, assets = session.Paths.AssetRoot },
                    view = session.View, settings = Settings()
                };
            case "query.submit":
            {
                using var stop = token.Register(session.Cancel);
                await session.SubmitAsync(Text(p, "text"), Flag(p, "force"), false, Number(p, "scroll"));
                return Position();
            }
            case "query.retry":
                using (token.Register(session.Cancel)) await session.RetryFailedAsync();
                return Position();
            case "data.usage": return await Data(() => Task.FromResult(new UserData(session.Paths.UserRoot).Usage()));
            case "data.inspect": return await Data(() => Task.FromResult(new UserData(session.Paths.UserRoot).Inspect(Text(p, "path"))));
            case "data.export":
                await Data(() => session.ManageDataAsync(async () => { await new UserData(session.Paths.UserRoot).ExportAsync(Text(p, "path"), session.Settings, session.History, token); return true; })); return new { };
            case "data.import":
            {
                var previous = session.QuerySettingsIdentity();
                var imported = await Data(() => session.ManageDataAsync(() => Task.FromResult(new UserData(session.Paths.UserRoot).Import(Text(p, "path"), session.Settings, session.History, Flag(p, "includeSettings")))));
                session.SettingsSaved(previous); return imported;
            }
            case "data.clear":
                await Data(() => session.ManageDataAsync(() => { new UserData(session.Paths.UserRoot).Clear(Text(p, "category")); return Task.FromResult(true); })); return new { };
            case "query.regenerate":
            {
                using var stop = token.Register(session.Cancel);
                await session.RegenerateAsync();
                return Position();
            }
            case "query.navigate":
            {
                var direction = Number(p, "direction");
                if (direction is not (-1 or 1)) throw new HostException("invalid_params", "direction must be -1 or 1.");
                using var stop = token.Register(session.Cancel);
                var page = await session.NavigateAsync((int)direction, Number(p, "scroll"));
                return new { page, view = Position() };
            }
            case "query.home": session.ShowHome(true, Number(p, "scroll")); return Position();
            case "query.cancel": session.Cancel(); return Position();
            case "query.view": return session.View;
            case "suggest": return new { suggestions = session.Suggest(Text(p, "text")) };
            case "query.preview": return await session.PreviewAsync(Text(p, "text"), token);
            case "query.candidate": return LookupCandidates.From(Text(p, "text", ""));
            case "history.list": return History(Text(p, "text", ""), (int)Number(p, "offset"));
            case "history.remove":
            {
                var query = Text(p, "query"); var translation = Flag(p, "translation");
                var entry = session.History.Entries.FirstOrDefault(e => e.Query == query && e.Translation == translation);
                return new { removed = entry != null && session.RemoveHistory(entry), history = History() };
            }
            case "text.models":
            {
                if (!session.Settings.Online) throw new HostException("offline", "请先开启在线补充，再读取模型列表。");
                var endpoint = Text(p, "baseUrl");
                var models = await text.ModelChoicesAsync(endpoint, TextCredentials(p), token);
                await InferenceOptions.Remember(session.Paths.UserRoot, CompatibleText.Provider, endpoint, models, token);
                return new { models };
            }
            case "inference.models":
            {
                if (!session.Settings.Online) throw new HostException("offline", "请先开启在线补充，再读取模型列表。");
                try
                {
                    var models = await InferenceOptions.ReadSubscriptions(session.Settings.CodexPath, token);
                    await InferenceOptions.RememberSubscriptions(session.Paths.UserRoot, models, token);
                    return Settings();
                }
                catch (Exception e) when (e is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception or IOException)
                { throw new HostException("model_discovery_failed", "无法读取订阅模型，请检查官方客户端版本、登录状态和网络。"); }
            }
            case "inference.catalog":
                // Returns once the background OAuth catalog read of this start has finished.
                await session.SubscriptionCatalog.WaitAsync(token);
                return Settings();
            case "inference.configure":
            {
                var model = InferenceOptions.Available(session.Paths.UserRoot, session.Settings).FirstOrDefault(m => m.Provider == Text(p, "provider") && m.Id == Text(p, "model"))
                    ?? throw new HostException("invalid_settings", "请先读取模型列表并选择模型。");
                var previous = session.QuerySettingsIdentity();
                try { InferenceOptions.Configure(session.Settings, model, Text(p, "effort", ""), Text(p, "speed", "")); }
                catch (InvalidOperationException e) { throw new HostException("invalid_settings", e.Message); }
                session.SettingsSaved(previous); return Settings();
            }
            case "text.credentials":
                // Process memory only; the front end owns Keychain storage. No secret is echoed.
                session.SetTextCredentials(Text(p, "baseUrl"), TextCredentials(p));
                return new { };
            case "settings.get": return Settings();
            case "settings.update": return UpdateSettings(p);
            case "settings.setOnline":
                if (!p.TryGetProperty("online", out var online) || online.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new HostException("invalid_params", "online must be true or false.");
                session.SetOnline(online.GetBoolean());
                return Settings();
            case "dictionaries.list":
            {
                var library = session.Library;
                return new { dictionaries = library.IsCompletedSuccessfully ? library.Result.Dictionaries : DictionaryCatalog.InspectAll(session.Paths.DataRoot),
                    folder = session.Paths.DataRoot };
            }
            case "connection.status": return await ProviderConnection.CheckAsync(Provider(p), Text(p, "codexPath", session.Settings.CodexPath), token);
            case "connection.loginCommand":
            {
                var provider = Provider(p);
                // The front end opens this in a terminal on an explicit click; the official client owns sign-in.
                if (ProviderConnection.Command(provider, Text(p, "codexPath", session.Settings.CodexPath), true) is not { } info) return null;
                return new { fileName = info.FileName, arguments = info.ArgumentList.ToArray(),
                    path = info.Environment.TryGetValue("PATH", out var path) ? path ?? "" : "",
                    clearEnvironment = provider == "claude" ? ProviderConnection.ClaudeClearedVariables : [] };
            }
            case "speech.models":
                return new { models = SpeechModels.Load(session.Paths.UserRoot, session.Settings).Select(m => new { m.Provider, m.Id, m.Name, local = SpeechCatalog.IsLocal(m.Provider) }) };
            case "speech.elevenModels":
                await Speech(() => SpeechModels.RefreshAsync(session.Paths.UserRoot, metadata, session.Settings.Online, Text(p, "key", ""), token));
                return new { };
            case "speech.voices": return new { voices = SpeechCatalog.Voices(Text(p, "provider"), Text(p, "model")) };
            // Keys arrive with each call from the front end's secure storage. The host never keeps,
            // logs or returns them, and speech errors carry only Core's wording.
            case "speech.elevenVoices":
                return new { voices = await Speech(() => speech.ElevenVoicesAsync(session.Settings.Online, Text(p, "key", ""), token)) };
            case "speech.cached":
            {
                var options = SpeechRequest(p);
                return new { path = await Speech(() => Task.FromResult(speech.Cached(Text(p, "text"), options))), local = SpeechCatalog.IsLocal(options.Provider) };
            }
            case "speech.synthesize":
            {
                var key = Text(p, "key", "");
                return new { path = await Speech(() => speech.OnlineAsync(Text(p, "text"), SpeechRequest(p), session.Settings.Online, () => key, null, token)) };
            }
            case "cancel":
            {
                if (!p.TryGetProperty("id", out var target) || target.ValueKind is not (JsonValueKind.Number or JsonValueKind.String))
                    throw new HostException("invalid_params", "id must name a running request.");
                var found = running.TryGetValue(target.GetRawText(), out var request);
                request?.Cancel();
                return new { cancelled = found };
            }
            default: throw new HostException("method_not_found", $"Unknown method '{method}'.");
        }
    }

    private object Position() { var view = session.View; return new { request = view.Request, revision = view.Revision }; }

    private object History(string text = "", int offset = 0) => new
    {
        total = session.History.Entries.Count(e => e.Query.Contains(text, StringComparison.OrdinalIgnoreCase)),
        entries = session.History.Search(text, offset).Select(e => new { e.Query, e.Translation, e.QueriedAt, e.Preview, e.Kind }).ToArray(),
        error = session.History.Error
    };

    private object Settings()
    {
        var settings = session.Settings;
        var models = InferenceOptions.Available(session.Paths.UserRoot, settings);
        return new
        {
            settings, models, judgeModels = ModelCatalog.JudgeChoices(models),
            controls = new { onlineLabel = ControlText.OnlineLabel(settings.Online), onlineTooltip = ControlText.OnlineTooltip(settings.Online),
                regenerateTooltip = ControlText.RegenerateTooltip(settings.Online) }
        };
    }

    private static CompatibleCredentials TextCredentials(JsonElement p) => new(Text(p, "clientId", ""), Text(p, "clientSecret", ""), Text(p, "apiKey", ""));
    private static SpeechOptions SpeechRequest(JsonElement p) => new(Text(p, "provider"), Text(p, "model"), Text(p, "voice"));
    // Backups and caches: Core's refusals are already worded for the user. Archive, JSON and file
    // system messages can be English or name local paths, so each of those gets one fixed sentence.
    private static async Task<T> Data<T>(Func<Task<T>> run)
    {
        try { return await run(); }
        catch (UserDataException e) { throw new HostException("invalid_data", e.Message); }
        catch (DataBusyException e) { throw new HostException("busy", e.Message); }
        catch (Exception e) when (e is InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new HostException("invalid_data", "所选文件不是有效的 LeXi 备份。"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new HostException("file_failed", "无法读写所选文件或数据文件夹，请检查位置和权限。"); }
    }

    private static async Task<T> Speech<T>(Func<Task<T>> run)
    {
        try { return await run(); }
        catch (Exception e) when (e is InvalidOperationException or InvalidDataException or TimeoutException) { throw new HostException("speech_failed", e.Message); }
        // File-system messages can contain local paths.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new HostException("speech_failed", SpeechAudio.StorageFailure); }
    }

    // Applies only the named settings, validates the result as the Windows dialog does, then saves.
    private object UpdateSettings(JsonElement p)
    {
        if (!p.TryGetProperty("settings", out var changes) || changes.ValueKind != JsonValueKind.Object)
            throw new HostException("invalid_params", "settings must be an object.");
        var merged = JsonSerializer.SerializeToNode(session.Settings, Options)!.AsObject();
        foreach (var property in changes.EnumerateObject())
        {
            if (!merged.ContainsKey(property.Name)) throw new HostException("invalid_params", $"Unknown setting '{property.Name}'.");
            merged[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        }
        var candidate = merged.Deserialize<AppSettings>(Options) ?? throw new HostException("invalid_params", "settings must be an object.");
        candidate.DisabledDictionaries ??= [];
        if (ModelCatalog.Validate(candidate) is { } error) throw new HostException("invalid_settings", error);
        // A speech choice is checked as the Windows dialog checks it before saving; other changes keep it as saved.
        if (changes.EnumerateObject().Any(c => c.Name.StartsWith("speech", StringComparison.Ordinal)))
        {
            try { SpeechCatalog.Validate("test", SpeechOptions.From(candidate)); }
            catch (InvalidOperationException e) { throw new HostException("invalid_settings", e.Message); }
        }
        SpeechPreferences.Remember(session.Settings);
        candidate.SpeechProfiles = session.Settings.SpeechProfiles; SpeechPreferences.Remember(candidate);
        var previous = session.QuerySettingsIdentity();
        foreach (var property in typeof(AppSettings).GetProperties().Where(x => x.CanRead && x.CanWrite))
            property.SetValue(session.Settings, property.GetValue(candidate));
        session.SettingsSaved(previous);
        return Settings();
    }

    private static string Provider(JsonElement p)
    {
        var provider = Text(p, "provider");
        return provider is "codex" or "claude" ? provider : throw new HostException("invalid_params", "provider must be codex or claude.");
    }
    private static string Text(JsonElement p, string name, string? fallback = null) =>
        p.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! :
        fallback ?? throw new HostException("invalid_params", $"Missing string parameter '{name}'.");
    private static bool Flag(JsonElement p, string name) =>
        p.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static double Number(JsonElement p, string name) =>
        p.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && double.IsFinite(value.GetDouble()) ? value.GetDouble() : 0;

    private void Reply(JsonElement? id, object? result) => Send(new { id, result });
    private void Fail(JsonElement? id, string code, string message) => Send(new { id, error = new { code, message } });
    private void Send(object message) => outgoing.Writer.TryWrite(JsonSerializer.Serialize(message, Options));

    private async Task WriteAsync()
    {
        try
        {
            await foreach (var line in outgoing.Reader.ReadAllAsync())
            {
                await output.WriteAsync(line);
                await output.WriteAsync('\n');
                if (!outgoing.Reader.TryPeek(out _)) await output.FlushAsync();
            }
            await output.FlushAsync();
        }
        catch (IOException) { /* The front end closed the pipe; the read loop ends on its own. */ }
    }
}
