import LeXiKit
import SwiftUI

struct CompatibleConnectionSettings: View {
    @Bindable var store: LexiStore
    private let keys: any TextCredentialStore
    @State private var baseUrl = ""
    @State private var clientId = ""
    @State private var clientSecret = ""
    @State private var apiKey = ""
    @State private var streaming = true
    @State private var saved = false
    @State private var message = "读取模型列表后，返回“查询”选择模型。"
    @State private var loading = false
    @State private var generation = 0
    @State private var request: Task<Void, Never>?

    init(store: LexiStore, keys: any TextCredentialStore = KeychainTextCredentials()) { self.store = store; self.keys = keys }
    var body: some View {
        Section {
            TextField("Base URL", text: $baseUrl)
            SecureField("CF Access Client ID", text: $clientId, prompt: Text(saved ? "留空保留" : "填写 Client ID"))
            SecureField("CF Access Client Secret", text: $clientSecret, prompt: Text(saved ? "留空保留" : "填写 Client Secret"))
            SecureField("API Key（可选）", text: $apiKey, prompt: Text(saved ? "留空保留" : "可以留空"))
            Toggle("逐片显示生成结果", isOn: $streaming)
            HStack {
                Button("读取模型列表") { load() }.disabled(loading)
                Button("保存连接") { save() }
                if saved { Button("清除凭据", role: .destructive) { remove() } }
                if loading { ProgressView().controlSize(.small) }
            }
            Text(message).font(.callout).foregroundStyle(.secondary).textSelection(.enabled)
        } header: { Text("API 连接") }
        footer: { Text("OpenAI 兼容接口。凭据按地址保存在本机钥匙串中，API Key 可留空。此入口只用于文本；发音独立配置。") }
        .onAppear {
            if baseUrl.isEmpty {
                baseUrl = store.settings?.settings.compatibleBaseUrl ?? "https://ai.rwby.io/v1"
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
                message = models.isEmpty ? "鉴权成功，服务端暂未提供可用模型。" : "已读取 \(models.count) 个模型。保存连接后，返回“查询”选择主要模型、第二模型或义项判断模型。"
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
                else { message = "连接已保存。返回“查询”选择要使用的模型。" }
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
