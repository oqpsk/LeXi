import Foundation
import Observation

// The app's single source of truth: the latest QueryView from Lexi.Host plus front-end state
// (disclosures, original-source expansion) that Windows also keeps per window and per lookup.
@MainActor @Observable
public final class LexiStore {
    public enum Phase: Equatable { case starting, ready, failed(String) }
    public static let supportedProtocol = 1

    public private(set) var phase: Phase = .starting
    public private(set) var view: QueryView = .starting
    public private(set) var settings: SettingsPayload?
    public private(set) var history: HistoryList = .empty
    public private(set) var suggestions: [String] = []
    public private(set) var inputPreview: InputPreview?
    @ObservationIgnored private var previewWork: Task<Void, Never>?
    private var previewRequest = 0
    public private(set) var dictionaries: DictionaryList?
    public private(set) var version = ""
    public private(set) var compatibleModels: [ModelChoice] = []
    private var compatibleModelsEndpoint = ""
    private let textKeys: any TextCredentialStore
    // Disclosures the reader opened or closed in this lookup, keyed by Core sense keys plus
    // ":examples", ":sources" or ":alternatives". Untouched disclosures use their default.
    public var details: [String: Bool] = [:]
    // Original dictionary text: shown directly, then collapsed once definitions arrive.
    public private(set) var localWrapped = false
    public var localExpanded = false
    public var onlineExpanded = true
    private var onlineCollapsible = true
    private var client: HostClient?
    private var suggestionRequest = 0

    public init(textKeys: any TextCredentialStore = KeychainTextCredentials()) { self.textKeys = textKeys }

#if os(macOS)
    public func start(executable: URL, arguments: [String]) async {
        await start(transport: ProcessTransport(executable: executable, arguments: arguments))
    }
#endif

    public func start(transport: any HostTransport) async {
        let client = HostClient(transport: transport) { [weak self] event in
            Task { @MainActor in self?.handle(event) }
        }
        self.client = client
        do {
            try await client.start()
            let info: HostInfo = try await client.call("initialize")
            guard info.protocolVersion == Self.supportedProtocol else {
                phase = .failed("查询服务版本不匹配，请重新安装 LeXi。"); return
            }
            version = info.version; settings = info.settings
            receive(info.view)
            phase = .ready
            await refreshHistory()
        } catch {
            phase = .failed("无法启动查询服务：\(error.localizedDescription)")
        }
    }

    public func stop() async { clearInputPreview(); await client?.stop() }

    // Model API keys for platforms without the subscription clients (iOS), from the Keychain.
    // The host keeps them in memory only (keys.set); an empty key removes it.
    @discardableResult public func setKey(_ provider: String, _ key: String) async -> Bool {
        (try? await client?.call("keys.set", ["provider": provider, "key": key], as: Nothing.self)) != nil
    }
    private struct Nothing: Decodable, Sendable {}
    public func failToStart(_ message: String) { phase = .failed(message) }

    private func handle(_ event: HostEvent) {
        switch event {
        case .view(let next): receive(next)
        case .exited: if phase != .starting { phase = .failed("查询服务已停止，请重新打开 LeXi。") }
        }
    }

    // Views can arrive faster than they are drawn; only newer revisions replace what is shown.
    public func receive(_ next: QueryView) {
        guard next.revision > view.revision else { return }
        let previous = view
        let fresh = next.request != previous.request
        view = next
        if fresh {
            if next.kind != .home { details.removeAll() }
            else { Task { await refreshHistory() } }
        }
        // Same rules as the Windows window: a redrawn source section starts expanded only when no
        // definitions are shown, collapses once definitions arrive, and reopens when none do.
        if fresh || previous.local != next.local { localWrapped = false; localExpanded = false }
        if fresh || previous.online != next.online { onlineExpanded = next.answers?.empty ?? true; onlineCollapsible = true }
        guard fresh || previous.answers != next.answers else { return }
        switch next.answers?.originals {
        case .collapse:
            if onlineCollapsible { onlineExpanded = false; onlineCollapsible = false }
            if !localWrapped, !next.local.isEmpty { localWrapped = true; localExpanded = false }
        case .expand: localExpanded = true; onlineExpanded = true
        default: break
        }
    }

    public func isOpen(_ key: String, default initial: Bool = false) -> Bool { details[key] ?? initial }
    public func setOpen(_ key: String, _ open: Bool) { details[key] = open }
    // A sense's references stay open when the reader was reading the matching fallback brief.
    public func sourcesOpenByDefault(_ sense: Sense) -> Bool { sense.referenceKeys.contains { details[$0] == true } }
    // Alternatives stay open when the reader was inside one of them before merging.
    public func alternativesOpenByDefault(_ sense: Sense) -> Bool {
        sense.alternatives.contains { alternative in details.contains { $0.value && $0.key.hasPrefix(alternative.key + ":") } }
    }

    // ---- Queries ----
    public func submit(_ text: String, force: Bool = false, scroll: Double = 0, context: String? = nil) async {
        clearSuggestions()
        await provideTextCredentials()
        _ = try? await client?.call("query.submit", Submit(text: text, force: force, scroll: scroll, context: context)) as SubmitResult?
        await refreshHistoryIfHome()
    }
    public func retryFailed() async { clearSuggestions(); await provideTextCredentials(); _ = try? await client?.call("query.retry") as SubmitResult? }
    public func regenerate() async { clearSuggestions(); await provideTextCredentials(); _ = try? await client?.call("query.regenerate") as SubmitResult? }
    public func navigate(_ direction: Int, scroll: Double = 0) async -> QueryPage? {
        clearSuggestions()
        await provideTextCredentials()
        let result = try? await client?.call("query.navigate", Navigate(direction: direction, scroll: scroll)) as NavigateResult?
        return result?.page
    }
    public func home(scroll: Double = 0) async {
        clearSuggestions()
        _ = try? await client?.call("query.home", Scroll(scroll: scroll)) as SubmitResult?
        await refreshHistory()
    }
    public func cancelQuery() async { _ = try? await client?.call("query.cancel") as SubmitResult? }

    // Candidates are discarded when a newer keystroke has already asked again.
    public func suggest(_ text: String) async {
        suggestionRequest += 1
        let request = suggestionRequest
        let result = try? await client?.call("suggest", ["text": text]) as Suggestions?
        if request == suggestionRequest { suggestions = result?.suggestions ?? [] }
    }
    public func clearSuggestions() { suggestionRequest += 1; suggestions = []; clearInputPreview() }

    // Only the shared, read-only preview endpoint runs after the typing pause. Cancellation
    // also cancels its host request; a late reply cannot repopulate a submitted/dismissed draft.
    public func previewInput(_ text: String) {
        clearInputPreview()
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return }
        let request = previewRequest
        previewWork = Task {
            do {
                try await Task.sleep(for: .milliseconds(300))
                let result = try await client?.callOptional("query.preview", ["text": text], as: InputPreview.self)
                if !Task.isCancelled && request == previewRequest { inputPreview = result }
            } catch { }
        }
    }
    // Core's rule for text taken from another app when LeXi is summoned (query.candidate).
    public func candidate(_ captured: CapturedText) async -> LookupCandidate? {
        try? await client?.callOptional("query.candidate", Candidate(text: captured.text, surrounding: captured.surrounding, start: captured.start),
                                         as: LookupCandidate.self)
    }
    private struct Candidate: Encodable, Sendable { let text: String; let surrounding: String?; let start: Int }
    public func clearInputPreview() {
        previewRequest += 1; previewWork?.cancel(); previewWork = nil; inputPreview = nil
    }

    // ---- History ----
    public private(set) var historyFilter = ""
    private var historyRequest = 0
    private var historyShown = 0
    private var historyPaging = false
    // A home view refreshes the list on its own while home() refreshes it too. A reply is dropped
    // only when a later one is already shown, so it never waits on a request still in flight.
    public func refreshHistory() async {
        historyRequest += 1; let request = historyRequest; let text = historyFilter
        if let list = try? await client?.call("history.list", ["text": text]) as HistoryList?,
           request > historyShown, text == historyFilter { history = list; historyShown = request }
    }
    public func searchHistory(_ text: String) async { historyFilter = text; await refreshHistory() }
    public func moreHistory() async {
        guard !historyPaging else { return }; historyPaging = true; defer { historyPaging = false }
        let shown = historyShown; let text = historyFilter; let offset = history.entries.count
        let parameters: [String: SettingValue] = ["text": .string(text), "offset": .integer(offset)]
        if let next = try? await client?.call("history.list", parameters) as HistoryList?, shown == historyShown, text == historyFilter, offset == history.entries.count {
            var list = HistoryList(entries: history.entries + next.entries, error: next.error); list.total = next.total; history = list
        }
    }
    public func dataUsage() async throws -> [CacheUsage] { try await requireClient().call("data.usage") }
    public func inspectBackup(_ path: String) async throws -> BackupPreview { try await requireClient().call("data.inspect", ["path": path]) }
    public func exportBackup(_ path: String) async throws { let _: EmptyResponse = try await requireClient().call("data.export", ["path": path]) }
    public func importBackup(_ path: String, includeSettings: Bool) async throws {
        let _: BackupPreview = try await requireClient().call("data.import", ["path": SettingValue.string(path), "includeSettings": .bool(includeSettings)])
        await refreshSettings(); await refreshHistory()
    }
    public func clearCache(_ category: String) async throws { let _: EmptyResponse = try await requireClient().call("data.clear", ["category": category]) }
    public func refreshSpeechModels(key: String) async throws { let _: EmptyResponse = try await requireClient().call("speech.elevenModels", ["key": key]) }
    private struct EmptyResponse: Decodable, Sendable {}
    private func requireClient() throws -> HostClient { guard let client else { throw CocoaError(.fileNoSuchFile) }; return client }
    private func refreshHistoryIfHome() async { if view.kind == .home { await refreshHistory() } }
    public func remove(_ entry: HistoryEntry) async {
        if let result = try? await client?.call("history.remove", RemoveHistory(query: entry.query, translation: entry.translation)) as HistoryRemoval? {
            history = result.history; historyShown = historyRequest
            if !historyFilter.isEmpty { await refreshHistory() }
        }
    }

    // ---- Settings ----
    public func setOnline(_ online: Bool) async {
        if let payload = try? await client?.call("settings.setOnline", ["online": online]) as SettingsPayload? { settings = payload }
    }
    // Returns the shared validation message when Lexi.Core rejects the change.
    @discardableResult
    public func update(_ changes: [String: SettingValue]) async -> String? {
        guard let client else { return nil }
        do { settings = try await client.call("settings.update", ["settings": changes]); return nil }
        catch let error as HostError { return error.message }
        catch { return error.localizedDescription }
    }
    public func configureInference(_ model: ModelChoice, effort: String, speed: String) async -> String? {
        guard let client else { return nil }
        do {
            settings = try await client.call("inference.configure", ["provider": model.provider, "model": model.id, "effort": effort, "speed": speed])
            return nil
        } catch let error as HostError { return error.message }
        catch { return error.localizedDescription }
    }
    public func readSubscriptionModels() async -> String? {
        guard let client else { return nil }
        do { settings = try await client.call("inference.models"); return nil }
        catch let error as HostError { return error.message }
        catch { return error.localizedDescription }
    }
    // The host reads the OAuth model catalog in the background after it starts; this returns the
    // settings once that read has finished, so an early settings window still gets every model.
    public func awaitModelCatalog() async {
        if let payload = try? await client?.call("inference.catalog") as SettingsPayload? { settings = payload }
    }
    public func refreshSettings() async {
        if let payload = try? await client?.call("settings.get") as SettingsPayload? { settings = payload }
    }
    public func refreshDictionaries() async {
        if let list = try? await client?.call("dictionaries.list") as DictionaryList? { dictionaries = list }
    }
    public func connectionStatus(_ provider: String) async -> LoginStatus? {
        try? await client?.call("connection.status", ["provider": provider]) as LoginStatus?
    }
    public func loginCommand(_ provider: String) async -> LoginCommand? {
        guard let client else { return nil }
        return try? await client.callOptional("connection.loginCommand", ["provider": provider], as: LoginCommand.self)
    }

    // ---- Custom text service ----
    public var queryModels: [ModelChoice] { withReadModels(settings?.models ?? []) }
    // The host's judges (Codex or the text endpoint on the Mac; the API services or the text endpoint
    // on iOS), with the endpoint models just read.
    public var judgeChoices: [ModelChoice] { withReadModels(settings?.judgeModels ?? []) }
    private func withReadModels(_ saved: [ModelChoice]) -> [ModelChoice] {
        guard let endpoint = settings?.settings.compatibleBaseUrl,
              (try? TextCredentials.endpoint(endpoint)) == compatibleModelsEndpoint else { return saved }
        return saved + compatibleModels.filter { model in !saved.contains(where: { $0.key == model.key }) }
    }
    public func clearCompatibleModels() { compatibleModels = []; compatibleModelsEndpoint = "" }
    public func textModels(_ baseUrl: String, credentials: TextCredentials) async throws -> [ModelChoice] {
        guard let client else { throw HostError.exited }
        let models = try await client.call("text.models", TextRequest(baseUrl: baseUrl, credentials: credentials), as: TextModels.self).models
        await refreshSettings()
        return models
    }
    public func keepTextModels(_ models: [ModelChoice], baseUrl: String) {
        compatibleModels = models; compatibleModelsEndpoint = (try? TextCredentials.endpoint(baseUrl)) ?? ""
    }
    public func forgetTextCredentials(_ baseUrl: String) async {
        _ = try? await client?.call("text.credentials", TextRequest(baseUrl: baseUrl, credentials: TextCredentials())) as TextConfigured?
    }
    private func provideTextCredentials() async {
        guard let settings = settings?.settings, settings.online,
              settings.provider == "compatible" || settings.parallelLookup && settings.secondaryProvider == "compatible" || settings.mergeSenses && settings.judgeProvider == "compatible",
              let endpoint = settings.compatibleBaseUrl else { return }
        // Missing/denied Keychain access passes no credentials: Core can still restore a saved view,
        // or show its explicit missing-credentials error if a network request is needed.
        let credentials = (try? textKeys.read(endpoint)) ?? TextCredentials()
        _ = try? await client?.call("text.credentials", TextRequest(baseUrl: endpoint, credentials: credentials)) as TextConfigured?
    }
    private struct TextRequest: Encodable, Sendable {
        let baseUrl: String
        let clientId: String
        let clientSecret: String
        let apiKey: String
        init(baseUrl: String, credentials: TextCredentials) {
            self.baseUrl = baseUrl; clientId = credentials.clientId; clientSecret = credentials.clientSecret; apiKey = credentials.apiKey
        }
    }
    private struct TextModels: Decodable, Sendable { let models: [ModelChoice] }
    private struct TextConfigured: Decodable, Sendable {}

    // ---- Speech ----
    public var speechSelection: SpeechSelection {
        let settings = settings?.settings
        return SpeechSelection(provider: settings?.speechProvider ?? "", model: settings?.speechModel ?? "", voice: settings?.speechVoice ?? "")
    }
    public func speechModels() async -> [SpeechModel] {
        (try? await client?.call("speech.models") as SpeechModels?)?.models ?? []
    }
    public func speechVoices(provider: String, model: String) async -> [SpeechVoice] {
        (try? await client?.call("speech.voices", ["provider": provider, "model": model]) as SpeechVoices?)?.voices ?? []
    }
    // The key is the one typed in settings or read from the Keychain; the host does not keep it.
    public func elevenVoices(key: String) async throws -> [SpeechVoice] {
        guard let client else { throw HostError.exited }
        return try await client.call("speech.elevenVoices", ["key": key], as: SpeechVoices.self).voices
    }
    public func cachedSpeech(_ text: String, _ selection: SpeechSelection) async throws -> SpeechCache {
        guard let client else { throw HostError.exited }
        return try await client.call("speech.cached", SpeechRequest(text: text, selection, key: nil))
    }
    public func synthesize(_ text: String, _ selection: SpeechSelection, key: String) async throws -> String {
        guard let client else { throw HostError.exited }
        return try await client.call("speech.synthesize", SpeechRequest(text: text, selection, key: key), as: SpeechFile.self).path
    }

    private struct Submit: Encodable, Sendable { let text: String; let force: Bool; let scroll: Double; let context: String? }
    private struct Navigate: Encodable, Sendable { let direction: Int; let scroll: Double }
    private struct Scroll: Encodable, Sendable { let scroll: Double }
    private struct RemoveHistory: Encodable, Sendable { let query: String; let translation: Bool }
    private struct SpeechModels: Decodable, Sendable { let models: [SpeechModel] }
    private struct SpeechVoices: Decodable, Sendable { let voices: [SpeechVoice] }
    private struct SpeechFile: Decodable, Sendable { let path: String }
    private struct SpeechRequest: Encodable, Sendable {
        let text: String, provider: String, model: String, voice: String, key: String?
        init(text: String, _ selection: SpeechSelection, key: String?) {
            self.text = text; provider = selection.provider; model = selection.model; voice = selection.voice; self.key = key
        }
    }
}

extension LexiStore: SpeechService {}
