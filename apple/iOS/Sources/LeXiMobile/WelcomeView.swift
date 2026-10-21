import LeXiKit
import SwiftUI

// Shown once, when LeXi first opens without an offline dictionary or a model service: what each is
// for, the dictionary to download and the service to connect. Both can wait; LeXi still looks words
// up in its dictionaries and Wiktionary.
struct WelcomeView: View {
    @Bindable var store: LexiStore
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Text("查单词、短语，或粘贴一段要翻译的文字。开始之前，可以先准备好下面两样；都能稍后在“设置”里再弄。")
                }
                Section {
                    ForEach(store.dictionaries?.packs ?? []) { pack in DictionaryPackRow(pack: pack, store: store) }
                } header: {
                    Text("离线词典")
                } footer: {
                    Text("输入时给出建议，打字时就能先看到 TL;DR，没有网络也能查基本释义。")
                }
                Section {
                    NavigationLink {
                        ServicesForm(store: store).navigationTitle("模型服务").navigationBarTitleDisplayMode(.inline)
                    } label: {
                        LabeledContent("连接模型服务", value: services.isEmpty ? "未设置" : services.joined(separator: " · "))
                    }
                } header: {
                    Text("模型服务")
                } footer: {
                    Text("LeXi 用你自己的 OpenAI 或 Anthropic 密钥，或与 OpenAI 兼容的自建服务，生成义项、例句和用法。密钥只保存在这台设备的钥匙串里。暂不连接也能用：查词时显示词典和 Wiktionary 的释义。")
                }
            }
            .navigationTitle("欢迎使用 LeXi")
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button("开始使用") { dismiss() } } }
            .task { await store.refreshDictionaries() }
        }
    }

    private var services: [String] { ModelKeys.services(store.settings?.settings) }
}
