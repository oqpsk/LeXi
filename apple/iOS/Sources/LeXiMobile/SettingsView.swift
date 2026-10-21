import LeXiKit
import SwiftUI

// iOS has no official subscription clients, so models are reached with the user's own API keys or
// custom text service. Keys stay in the Keychain on this device and reach Lexi.Core in memory only.
struct SettingsView: View {
    @Bindable var store: LexiStore
    var speech: SpeechController
    @Environment(\.dismiss) private var dismiss
    @AppStorage(Appearance.key) private var appearance = Appearance.system.rawValue

    var body: some View {
        NavigationStack {
            Form {
                KeysSection(store: store)
                CustomServiceSection(store: store)
                if let payload = store.settings { ModelSection(store: store, payload: payload) }
                SpeechSection(store: store, speech: speech)
                DictionarySection(store: store)
                Section("外观") {
                    Picker("外观", selection: $appearance) {
                        ForEach(Appearance.allCases) { Text($0.title).tag($0.rawValue) }
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()
                }
                Section {
                    LabeledContent("版本", value: store.version.isEmpty ? "—" : store.version)
                    SourceLink("词库与许可", url: "https://github.com/oqpsk/LeXi/blob/main/THIRD_PARTY_NOTICES.md")
                } footer: {
                    Text("LeXi 不需要注册，不收集使用数据。只有查 Wiktionary、调用你选的模型和合成在线语音时才会联网。")
                }
            }
            .navigationTitle("设置")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button("完成") { dismiss() } } }
            .task { await store.refreshSettings(); await store.refreshDictionaries() }
        }
    }
}

// One secure field per service. The OpenAI key also serves OpenAI speech.
struct KeysSection: View {
    @Bindable var store: LexiStore
    @State private var inputs: [String: String] = [:]
    @State private var saved: Set<String> = []
    @State private var message = ""
    private let keys = KeychainSpeechKeys()

    var body: some View {
        Section {
            ForEach(ModelKeys.providers, id: \.self) { provider in
                VStack(alignment: .leading, spacing: 6) {
                    HStack {
                        Text(provider == "openai" ? "OpenAI" : "Anthropic").font(.body.weight(.medium))
                        Spacer()
                        Label(saved.contains(provider) ? "已保存" : "未填写", systemImage: saved.contains(provider) ? "checkmark.circle.fill" : "circle")
                            .labelStyle(.titleAndIcon).font(.caption)
                            .foregroundStyle(saved.contains(provider) ? AnyShapeStyle(.green) : AnyShapeStyle(.secondary))
                    }
                    SecureField(saved.contains(provider) ? "留空保留已有密钥" : "粘贴 API Key",
                                text: Binding(get: { inputs[provider] ?? "" }, set: { inputs[provider] = $0 }))
                        .textInputAutocapitalization(.never).autocorrectionDisabled()
                        .onSubmit { save(provider) }
                    HStack {
                        Button("保存") { save(provider) }.disabled((inputs[provider] ?? "").trimmingCharacters(in: .whitespaces).isEmpty)
                        Spacer()
                        Button("清除", role: .destructive) { remove(provider) }.disabled(!saved.contains(provider))
                    }
                    .buttonStyle(.borderless)
                }
                .padding(.vertical, 4)
            }
            if !message.isEmpty { Label(message, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.red).font(.callout) }
        } header: {
            Text("模型服务")
        } footer: {
            Text("iOS 上没有官方客户端，LeXi 用你自己的 API Key 访问 OpenAI 与 Anthropic，按服务商的 API 计费，与 ChatGPT、Claude 订阅分开；也可以在下方接入自定义文本服务。密钥只保存在本机钥匙串。OpenAI 的密钥也用于 OpenAI 在线语音。")
        }
        .onAppear { saved = Set(ModelKeys.providers.filter { keys.hasSaved($0) }) }
    }

    private func save(_ provider: String) {
        let value = (inputs[provider] ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.isEmpty else { return }
        do {
            try keys.save(provider, value)
            inputs[provider] = nil; saved.insert(provider); message = ""
            Task { await store.setKey(provider, value) }
        } catch { message = SpeechController.message(error) }
    }

    private func remove(_ provider: String) {
        do {
            try keys.remove(provider)
            saved.remove(provider); message = ""
            Task { await store.setKey(provider, "") }
        } catch { message = SpeechController.message(error) }
    }
}

// The custom text service, as on the Mac: an OpenAI-compatible endpoint. Its credentials are kept per
// address in this device's Keychain; nothing comes over from the Mac.
struct CustomServiceSection: View {
    @Bindable var store: LexiStore
    private let keys: any TextCredentialStore = KeychainTextCredentials()
    @State private var baseUrl = ""
    @State private var clientId = ""
    @State private var clientSecret = ""
    @State private var apiKey = ""
    @State private var streaming = true
    @State private var saved = false
    @State private var message = "读取模型列表后，在下方“查询”中选择模型。"
    @State private var loading = false
    @State private var generation = 0
    @State private var request: Task<Void, Never>?

    var body: some View {
        Section {
            TextField("Base URL", text: $baseUrl, prompt: Text("Base URL，例如 https://…/v1"))
                .keyboardType(.URL).textContentType(.URL)
            SecureField("CF Access Client ID", text: $clientId, prompt: Text(saved ? "CF Access Client ID · 留空保留" : "CF Access Client ID"))
            SecureField("CF Access Client Secret", text: $clientSecret, prompt: Text(saved ? "CF Access Client Secret · 留空保留" : "CF Access Client Secret"))
            SecureField("API Key", text: $apiKey, prompt: Text(saved ? "API Key · 留空保留" : "API Key（可选）"))
            Toggle("逐片显示生成结果", isOn: $streaming)
            HStack {
                Button("读取模型列表") { load() }.disabled(loading || baseUrl.isEmpty)
                if loading { ProgressView().padding(.leading, 6) }
                Spacer()
                Button("保存连接") { save() }.disabled(baseUrl.isEmpty)
            }
            .buttonStyle(.borderless)
            if saved { Button("清除此地址的凭据", role: .destructive) { remove() } }
            Text(message).font(.callout).foregroundStyle(.secondary).textSelection(.enabled)
        } header: {
            Text("自定义文本服务")
        } footer: {
            Text("OpenAI 兼容接口，可以填 Mac 上用的同一个地址。凭据按地址保存在本机钥匙串，不会从 Mac 同步；API Key 可留空。只用于文本，发音另行设置。")
        }
        .textInputAutocapitalization(.never)
        .autocorrectionDisabled()
        .onAppear {
            if baseUrl.isEmpty {
                baseUrl = store.settings?.settings.compatibleBaseUrl ?? ""
                streaming = store.settings?.settings.compatibleStreaming ?? true
                saved = keys.hasSaved(baseUrl)
            }
        }
        .onChange(of: baseUrl) { old, _ in
            // Filling in the saved address on appear is not an edit: keep the opening guidance.
            guard !old.isEmpty else { return }
            invalidate(); clientId = ""; clientSecret = ""; apiKey = ""; saved = keys.hasSaved(baseUrl)
        }
        .onChange(of: clientId) { _, _ in invalidate() }
        .onChange(of: clientSecret) { _, _ in invalidate() }
        .onChange(of: apiKey) { _, _ in invalidate() }
        .onDisappear { request?.cancel() }
    }
    private func invalidate() {
        generation += 1; request?.cancel(); loading = false; store.clearCompatibleModels()
        message = "请读取模型列表，再选择要使用的模型。"
    }
    private func credentials(_ endpoint: String) throws -> TextCredentials {
        let previous: TextCredentials
        if !clientId.isEmpty, !clientSecret.isEmpty { previous = (try? keys.read(endpoint)) ?? TextCredentials() }
        else { previous = try keys.read(endpoint) ?? TextCredentials() }
        let value = TextCredentials(clientId: clientId.isEmpty ? previous.clientId : clientId,
            clientSecret: clientSecret.isEmpty ? previous.clientSecret : clientSecret, apiKey: apiKey.isEmpty ? previous.apiKey : apiKey)
        try value.validate(); return value
    }
    private func load() {
        request?.cancel(); generation += 1
        let current = generation
        loading = true; message = "正在读取模型列表…"
        request = Task { @MainActor in
            defer { if current == generation { loading = false } }
            do {
                let endpoint = try TextCredentials.endpoint(baseUrl)
                let models = try await store.textModels(endpoint, credentials: credentials(endpoint))
                guard !Task.isCancelled, current == generation else { return }
                store.keepTextModels(models, baseUrl: endpoint)
                message = models.isEmpty ? "鉴权成功，服务端暂未提供可用模型。" : "已读取 \(models.count) 个模型。保存连接后，在下方“查询”中选择主要模型、第二个模型或义项判断模型。"
            } catch {
                if !Task.isCancelled, current == generation { message = error.localizedDescription }
            }
        }
    }
    private func save() {
        do {
            let endpoint = try TextCredentials.endpoint(baseUrl)
            try keys.save(endpoint, credentials(endpoint))
            saved = true
            Task { @MainActor in
                if let error = await store.update(["compatibleBaseUrl": .string(endpoint), "compatibleStreaming": .bool(streaming)]) { message = error }
                else { message = "连接已保存。在下方“查询”中选择要使用的模型。" }
            }
        } catch { message = error.localizedDescription }
    }
    private func remove() {
        do {
            try keys.remove(baseUrl); invalidate(); clientId = ""; clientSecret = ""; apiKey = ""; saved = false
            let endpoint = baseUrl
            Task { @MainActor in
                await store.forgetTextCredentials(endpoint)
                message = "已清除此地址的凭据。"
            }
        } catch { message = error.localizedDescription }
    }
}

struct ModelSection: View {
    @Bindable var store: LexiStore
    let payload: SettingsPayload
    @State private var error: String?

    var body: some View {
        let settings = payload.settings
        Section {
            Toggle("在线补充", isOn: binding(settings.online, "online"))
            Toggle("自动补充双语释义", isOn: binding(settings.autoAi, "autoAi"))
            picker("主要模型", store.queryModels, selected: settings.provider + ":" + settings.model) { ["provider": .string($0.provider), "model": .string($0.id)] }
            inferenceControls(store.queryModels, selected: settings.provider + ":" + settings.model)
            Toggle("双模型同时查词", isOn: binding(settings.parallelLookup, "parallelLookup"))
            if settings.parallelLookup {
                picker("第二个模型", store.queryModels, selected: secondaryKey(settings)) { ["secondaryProvider": .string($0.provider), "secondaryModel": .string($0.id)] }
                inferenceControls(store.queryModels, selected: secondaryKey(settings))
            }
            Toggle("自动整理同类义项", isOn: binding(settings.mergeSenses, "mergeSenses"))
            if settings.mergeSenses {
                picker("义项判断模型", store.judgeChoices, selected: judgeKey(settings)) { ["judgeProvider": .string($0.provider), "judgeModel": .string($0.id)] }
                inferenceControls(store.judgeChoices, selected: judgeKey(settings))
            }
            if let error { Label(error, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.red).font(.callout) }
        } header: {
            Text("查询")
        } footer: {
            Text("两个模型独立查词，先返回的释义先显示；缺少某个服务的密钥时，只有那个模型无法查询。思考深度和速度跟着模型保存，同一模型在不同位置共用一套。")
        }
    }

    private func secondaryKey(_ settings: AppSettings) -> String {
        settings.secondaryProvider.isEmpty
            ? (settings.provider == "anthropic" ? "openai:gpt-6-luna" : "anthropic:claude-haiku-5-5")
            : settings.secondaryProvider + ":" + settings.secondaryModel
    }
    // As Lexi.Core's judge: the saved provider, or, for a judge saved before it was stored, the model id's service.
    private func judgeKey(_ settings: AppSettings) -> String {
        let provider = settings.judgeProvider ?? ""
        let service = ["openai", "anthropic", "compatible"].contains(provider) ? provider : settings.judgeModel.hasPrefix("claude") ? "anthropic" : "openai"
        return service + ":" + settings.judgeModel
    }

    private func binding(_ value: Bool, _ key: String) -> Binding<Bool> {
        Binding(get: { value }, set: { apply([key: .bool($0)]) })
    }

    private func picker(_ title: String, _ choices: [ModelChoice], selected: String, change: @escaping (ModelChoice) -> [String: SettingValue]) -> some View {
        Picker(title, selection: Binding(get: { selected }, set: { key in
            if let choice = choices.first(where: { $0.key == key }) { apply(change(choice)) }
        })) {
            ForEach(choices, id: \.key) { Text($0.label).tag($0.key) }
        }
    }

    // One row under each model picker, so the effort and speed it shows belong to that model.
    @ViewBuilder
    private func inferenceControls(_ models: [ModelChoice], selected: String) -> some View {
        if let model = models.first(where: { $0.key == selected }), let options = model.options {
            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text("思考深度 · 速度").lineLimit(1)
                    Spacer(minLength: 8)
                    inferencePicker("思考深度", options.effortChoices, value: model.effort ?? "") { value in
                        Task { error = await store.configureInference(model, effort: value, speed: model.speed ?? "") }
                    }
                    inferencePicker("速度", options.speedChoices, value: model.speed ?? "") { value in
                        Task { error = await store.configureInference(model, effort: model.effort ?? "", speed: value) }
                    }
                }
                if !options.note.isEmpty { Text(options.note).font(.caption).foregroundStyle(.secondary) }
            }
        }
    }
    private func inferencePicker(_ title: String, _ options: [InferenceOption], value: String, change: @escaping (String) -> Void) -> some View {
        Picker(title, selection: Binding(get: { value }, set: { change($0) })) {
            ForEach(options) { Text($0.label).tag($0.id) }
            if !options.contains(where: { $0.id == value }) { Text(value + "（当前不可用）").tag(value) }
        }
        .labelsHidden()
        .pickerStyle(.menu)
        .fixedSize()
        // Only the default is on offer, and nothing else was saved earlier.
        .disabled(options.count <= 1 && value.isEmpty)
    }

    private func apply(_ changes: [String: SettingValue]) { Task { error = await store.update(changes) } }
}

// Speech, as on the Mac: system voices, OpenAI, Grok and ElevenLabs. Keys stay in this device's
// Keychain (the OpenAI key above also serves speech); ElevenLabs reads the account's voices or takes
// a Voice ID.
struct SpeechSection: View {
    @Bindable var store: LexiStore
    var speech: SpeechController
    private let keys = KeychainSpeechKeys()
    @State private var models: [SpeechModel] = []
    @State private var catalog: [String: [SpeechVoice]] = [:]
    @State private var systemVoices: [SpeechVoice] = []
    @State private var accountVoices: [SpeechVoice]?
    @State private var lastEleven: SpeechVoice?
    @State private var manualVoice = ""
    @State private var keyInput = ""
    @State private var savedKeys: Set<String> = []
    @State private var voiceStatus = ""
    @State private var loadingModels = false
    @State private var loadingVoices = false
    @State private var voiceRequest = 0
    @State private var voiceTask: Task<Void, Never>?
    @State private var message = ""

    var body: some View {
        Group {
            if let settings = store.settings?.settings, let model = selected(settings) {
                Section {
                    Picker("发音模型", selection: Binding(get: { model.key }, set: { key in
                        if let next = models.first(where: { $0.key == key }) { choose(next, settings) }
                    })) { ForEach(models, id: \.key) { Text($0.name).tag($0.key) } }
                    let voices = voices(model, settings)
                    Picker("声线", selection: Binding(get: { settings.speechVoice }, set: { id in
                        if let voice = voices.first(where: { $0.id == id }) { manualVoice = ""; apply(model, voice) }
                    })) { ForEach(voices) { Text($0.name).tag($0.id) } }
                    if model.provider == "elevenlabs" { elevenControls(model) }
                    Button { preview(model, settings) } label: {
                        Label(speech.speakingID == "preview" ? "停止" : "试听", systemImage: speech.speakingID == "preview" ? "stop.fill" : "play.fill")
                    }
                    if speech.speakingID == "preview" || !speech.status.isEmpty, !speech.status.isEmpty {
                        Text(speech.status).font(.callout).foregroundStyle(.secondary)
                    }
                    if !message.isEmpty { Label(message, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.red).font(.callout) }
                } header: {
                    Text("发音")
                } footer: {
                    Text(info(model))
                }
                if model.provider == "xai" || model.provider == "elevenlabs" { keySection(model.provider) }
            } else {
                Section("发音") { ProgressView() }
            }
        }
        .task { await load() }
        .onChange(of: keyInput) { if store.settings?.settings.speechProvider == "elevenlabs" { invalidateVoices() } }
        .onDisappear { cancelVoiceLoading(); keyInput = "" }
    }

    @ViewBuilder private func elevenControls(_ model: SpeechModel) -> some View {
        HStack {
            Button(accountVoices == nil ? "读取声线列表" : "刷新声线列表") { loadVoices() }.disabled(loadingVoices)
            if loadingVoices { ProgressView().padding(.leading, 6) }
            Spacer()
            Button("刷新语音模型") {
                Task {
                    loadingModels = true; defer { loadingModels = false }
                    do { try await store.refreshSpeechModels(key: typedKey ?? keys.read("elevenlabs") ?? ""); await load() }
                    catch { message = SpeechController.message(error) }
                }
            }.disabled(loadingModels)
        }
        .buttonStyle(.borderless)
        Text(voiceStatusText).font(.caption).foregroundStyle(.secondary)
        TextField("Voice ID", text: $manualVoice, prompt: Text("指定其他声线：粘贴 Voice ID"))
            .textInputAutocapitalization(.never).autocorrectionDisabled()
            .onSubmit { applyManual(model) }
    }

    private func keySection(_ provider: String) -> some View {
        let saved = savedKeys.contains(provider)
        return Section {
            SecureField(KeychainSpeechKeys.title(provider), text: $keyInput, prompt: Text(saved ? "留空保留已有密钥" : "粘贴 API Key"))
                .textInputAutocapitalization(.never).autocorrectionDisabled()
                .onSubmit { saveKey(provider) }
            HStack {
                Label(saved ? "已保存" : "未填写", systemImage: saved ? "checkmark.circle.fill" : "circle")
                    .font(.caption).foregroundStyle(saved ? AnyShapeStyle(.green) : AnyShapeStyle(.secondary))
                Spacer()
                Button("清除", role: .destructive) { removeKey(provider) }.disabled(!saved)
                Button("保存") { saveKey(provider) }.disabled(typedKey == nil)
            }
            .buttonStyle(.borderless)
        } header: {
            Text(provider == "xai" ? "Grok 发音密钥" : "ElevenLabs 发音密钥")
        } footer: {
            Text(provider == "elevenlabs" ? "粘贴创建时复制的完整 Secret Key；API Key ID 无法用于连接。密钥只保存在本机钥匙串，不会从 Mac 同步。" : "密钥只保存在本机钥匙串，不会从 Mac 同步。")
        }
    }

    private var voiceStatusText: String {
        if !voiceStatus.isEmpty { return voiceStatus }
        guard let accountVoices else { return "读取声线列表即可选择账号可用的声线；读取不会生成音频。" }
        return accountVoices.isEmpty ? "账号未返回可用声线，可粘贴 Voice ID。" : "已读取 \(accountVoices.count) 条可用声线。"
    }

    private func info(_ model: SpeechModel) -> String {
        if model.local { return "本机声线离线可用，“按文字语言自动选择”为中文或英文挑选声线。生成过的在线发音会缓存。" }
        switch model.provider {
        case "xai": return "Grok TTS 的模型由服务管理，可选择声线；使用单独的 API Key 与 API 额度。"
        case "elevenlabs": return "使用 ElevenLabs 账号额度。ElevenReader 套餐与此分开。"
        default: return "使用 OpenAI 官方语音接口和上方的 OpenAI API Key，声线随模型切换。"
        }
    }

    private func selected(_ settings: AppSettings) -> SpeechModel? {
        models.first { $0.provider == settings.speechProvider && $0.id == settings.speechModel } ?? models.first
    }

    // The saved voice stays listed even when it is not installed or not in a loaded list.
    private func voices(_ model: SpeechModel, _ settings: AppSettings) -> [SpeechVoice] {
        let current = model.provider == settings.speechProvider && model.id == settings.speechModel
        let saved = SpeechVoice(id: settings.speechVoice, name: settings.speechVoiceName.isEmpty ? "已保存的声线" : settings.speechVoiceName)
        if model.provider == "elevenlabs" {
            return SpeechChoices.eleven(loaded: accountVoices, saved: current ? saved : lastEleven, initial: catalog[model.key] ?? [])
        }
        let list = model.local ? systemVoices : catalog[model.key] ?? []
        return current && !list.contains(where: { $0.id == saved.id }) ? [saved] + list : list
    }

    private func load() async {
        systemVoices = SystemVoices.choices()
        savedKeys = Set(KeychainSpeechKeys.providers.filter { keys.hasSaved($0) })
        let list = await store.speechModels()
        var voices: [String: [SpeechVoice]] = [:]
        for model in list where !model.local { voices[model.key] = await store.speechVoices(provider: model.provider, model: model.id) }
        catalog = voices
        if let settings = store.settings?.settings, settings.speechProvider == "elevenlabs" {
            lastEleven = SpeechVoice(id: settings.speechVoice, name: settings.speechVoiceName.isEmpty ? "已保存的声线" : settings.speechVoiceName)
        }
        models = list
    }

    // Same voice rules as the Mac: ElevenLabs returns to its last voice, the saved model keeps its
    // saved voice, and any other model starts with its first voice.
    private func choose(_ next: SpeechModel, _ settings: AppSettings) {
        if next.provider != settings.speechProvider { cancelVoiceLoading(); voiceStatus = ""; manualVoice = ""; keyInput = "" }
        speech.stop()
        let remembered = settings.speechProfiles?.first { $0.provider == next.provider && $0.model == next.id }
        let preferred = remembered?.voice ?? (next.provider == "elevenlabs" ? lastEleven?.id
            : next.provider == settings.speechProvider && next.id == settings.speechModel ? settings.speechVoice : nil)
        let available = voices(next, settings)
        guard let voice = available.first(where: { $0.id == preferred }) ?? available.first else { return }
        apply(next, voice)
    }

    private func apply(_ model: SpeechModel, _ voice: SpeechVoice) {
        speech.stop()
        Task { _ = await update(model, voice) }
    }

    private func update(_ model: SpeechModel, _ voice: SpeechVoice) async -> Bool {
        let error = await store.update(["speechProvider": .string(model.provider), "speechModel": .string(model.id),
                                        "speechVoice": .string(voice.id), "speechVoiceName": .string(voice.name)])
        message = error ?? ""
        if error == nil, model.provider == "elevenlabs" { lastEleven = voice }
        return error == nil
    }

    private func applyManual(_ model: SpeechModel) {
        let id = manualVoice.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !id.isEmpty else { return }
        speech.stop()
        Task { if await update(model, SpeechVoice(id: id, name: "自定义声线")) { manualVoice = "" } }
    }

    // A Voice ID typed but not yet applied is previewed, as on the Mac.
    private func preview(_ model: SpeechModel, _ settings: AppSettings) {
        if speech.speakingID == "preview" { speech.stop(); return }
        let manual = manualVoice.trimmingCharacters(in: .whitespacesAndNewlines)
        let voice = model.provider == "elevenlabs" && !manual.isEmpty ? manual : settings.speechVoice
        speech.play("Hello, this is LeXi.", SpeechSelection(provider: model.provider, model: model.id, voice: voice),
                    key: model.local ? nil : typedKey, using: store, finished: "试听结束", id: "preview")
    }

    private func loadVoices() {
        guard !loadingVoices else { return }
        cancelVoiceLoading()
        let request = voiceRequest
        loadingVoices = true
        voiceStatus = "正在读取可用声线…"
        let typed = typedKey
        voiceTask = Task {
            do {
                let key = try typed ?? keys.read("elevenlabs") ?? ""
                let loaded = try await store.elevenVoices(key: key)
                guard request == voiceRequest else { return }
                accountVoices = loaded
                voiceStatus = loaded.isEmpty ? "账号未返回可用声线，可粘贴 Voice ID。" : "已读取 \(loaded.count) 条可用声线，请选择后试听。"
            } catch {
                if request == voiceRequest { voiceStatus = SpeechController.message(error) }
            }
            if request == voiceRequest { loadingVoices = false }
        }
    }

    private func cancelVoiceLoading() { voiceRequest += 1; voiceTask?.cancel(); voiceTask = nil; loadingVoices = false }

    // A changed key makes the loaded account voices stale.
    private func invalidateVoices() {
        cancelVoiceLoading(); accountVoices = nil
        voiceStatus = "连接信息已更改，可重新读取声线列表。"
    }

    private var typedKey: String? {
        let value = keyInput.trimmingCharacters(in: .whitespacesAndNewlines)
        return value.isEmpty ? nil : value
    }

    private func saveKey(_ provider: String) {
        guard let value = typedKey else { return }
        do { try keys.save(provider, value); keyInput = ""; savedKeys.insert(provider); message = "" }
        catch { message = SpeechController.message(error) }
    }

    private func removeKey(_ provider: String) {
        do {
            try keys.remove(provider); savedKeys.remove(provider); message = ""
            if provider == "elevenlabs" { invalidateVoices() }
        } catch { message = SpeechController.message(error) }
    }
}

struct DictionarySection: View {
    @Bindable var store: LexiStore
    var body: some View {
        Section {
            if let list = store.dictionaries, !list.dictionaries.isEmpty {
                ForEach(list.dictionaries) { dictionary in
                    VStack(alignment: .leading, spacing: 2) {
                        Text(dictionary.name)
                        Text("\(dictionary.languages) · \(dictionary.entryCount) 条").font(.caption).foregroundStyle(.secondary)
                    }
                }
            } else {
                Text("尚未安装离线词库").foregroundStyle(.secondary)
            }
        } header: {
            Text("离线词库")
        } footer: {
            Text("没有离线词库时，LeXi 仍可查 Wiktionary 与模型释义。iOS 版暂不能下载词库。")
        }
    }
}
