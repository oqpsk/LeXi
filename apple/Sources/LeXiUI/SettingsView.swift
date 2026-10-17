import AppKit
import LeXiKit
import SwiftUI

// macOS settings apply as soon as they change. Lexi.Core validates each change with the same
// rules and messages as the Windows dialog; rejected changes leave the saved settings intact.
public struct SettingsView: View {
    @Bindable var store: LexiStore
    public init(store: LexiStore) { self.store = store }

    public var body: some View {
        TabView {
            Tab("查询与模型", systemImage: "text.magnifyingglass") { QuerySettings(store: store) }
            Tab("离线词典", systemImage: "books.vertical") { DictionarySettings(store: store) }
            Tab("账号连接", systemImage: "person.badge.key") { ConnectionSettings(store: store) }
            Tab("发音", systemImage: "speaker.wave.2") { SpeechSettings() }
        }
        .frame(width: 560, height: 520)
        .task { await store.refreshSettings() }
    }
}

struct QuerySettings: View {
    @Bindable var store: LexiStore
    @State private var error: String?

    var body: some View {
        if let payload = store.settings {
            let settings = payload.settings
            Form {
                Section {
                    toggle("在线补充", settings.online, "online")
                    toggle("自动补充双语释义", settings.autoAi, "autoAi")
                } footer: {
                    Note("使用本机 Claude Code / Codex 的订阅登录。两个模型独立查词，先返回的释义先显示。文本翻译使用主要模型。查过的内容直接使用缓存。切换模型后，点击“重新生成”才会更新已有结果。")
                }
                Section {
                    modelPicker("主要模型 · 查词与翻译", payload.models, selected: settings.provider + ":" + settings.model) { choice in
                        ["provider": .string(choice.provider), "model": .string(choice.id)]
                    }
                    toggle("双模型同时查词", settings.parallelLookup, "parallelLookup")
                    modelPicker("第二个查询模型", payload.models, selected: secondaryKey(settings)) { choice in
                        ["secondaryProvider": .string(choice.provider), "secondaryModel": .string(choice.id)]
                    }
                    .disabled(!settings.parallelLookup)
                    toggle("自动整理同类义项（包含单模型内部）", settings.mergeSenses, "mergeSenses")
                    modelPicker("义项判断模型 · 使用 Codex 订阅", payload.judgeModels, selected: "codex:" + settings.judgeModel) { choice in
                        ["judgeModel": .string(choice.id)]
                    }
                    .disabled(!settings.mergeSenses)
                    if let error { Text(error).foregroundStyle(LexiTheme.warm) }
                } footer: {
                    Note("同一词义的细分用法可归为一组，优先展示涵盖更完整的原文；其他表述保留在“用法细分”中。不同含义和分歧仍分开。Claude 关闭额外思考，释义生成后立即显示。Codex 使用 Low + Fast，优先速度并消耗更多订阅额度。")
                }
                Section {
                    Note("Return 查询 · Shift-Return 换行 · Esc 收起建议\n⌘L 再查一个 · ⌘R 重新生成 · ⌘[ 后退 · ⌘] 前进 · ⌘Y 查询历史 · ⌘, 设置")
                }
            }
            .formStyle(.grouped)
        } else {
            ProgressView()
        }
    }

    // The secondary model defaults to the other service's fast model, as in Lexi.Core.
    private func secondaryKey(_ settings: AppSettings) -> String {
        settings.secondaryProvider.isEmpty
            ? (settings.provider == "claude" ? "codex:gpt-6-luna" : "claude:haiku")
            : settings.secondaryProvider + ":" + settings.secondaryModel
    }

    private func toggle(_ label: String, _ value: Bool, _ key: String) -> some View {
        Toggle(label, isOn: Binding(get: { value }, set: { apply([key: .bool($0)]) }))
    }

    private func modelPicker(_ label: String, _ choices: [ModelChoice], selected: String,
                             change: @escaping (ModelChoice) -> [String: SettingValue]) -> some View {
        Picker(label, selection: Binding(get: { selected }, set: { key in
            if let choice = choices.first(where: { $0.key == key }) { apply(change(choice)) }
        })) {
            ForEach(choices, id: \.key) { Text($0.label).tag($0.key) }
        }
    }

    private func apply(_ changes: [String: SettingValue]) {
        Task { error = await store.update(changes) }
    }
}

struct DictionarySettings: View {
    @Bindable var store: LexiStore

    var body: some View {
        Form {
            if let payload = store.settings {
                Section {
                    Toggle("启用离线查询", isOn: Binding(get: { payload.settings.offlineEnabled },
                        set: { value in Task { await store.update(["offlineEnabled": .bool(value)]) } }))
                } footer: { Note("启用的词典用于本地查询与输入建议。取消勾选即可停用，词库文件会保留。") }
                if let list = store.dictionaries {
                    Section("已安装 \(list.dictionaries.filter(\.available).count) 部词典") {
                        ForEach(list.dictionaries) { dictionary in
                            VStack(alignment: .leading, spacing: 4) {
                                Toggle(dictionary.name, isOn: Binding(
                                    get: { dictionary.available && !payload.settings.disabledDictionaries.contains(dictionary.id) },
                                    set: { enabled in
                                        var disabled = payload.settings.disabledDictionaries.filter { $0 != dictionary.id }
                                        if !enabled { disabled.append(dictionary.id) }
                                        Task { await store.update(["disabledDictionaries": .strings(disabled)]) }
                                    }))
                                .disabled(!dictionary.available)
                                Text(dictionary.languages).font(.callout)
                                Text("\(dictionary.status) · \(dictionary.entryCount.formatted()) 个检索词条 · \(ByteCountFormatter.string(fromByteCount: dictionary.bytes, countStyle: .file))")
                                    .font(.caption).foregroundStyle(.secondary)
                                Text("\(dictionary.attribution) · 版本：\(dictionary.version) · 许可：\(dictionary.license)").font(.caption).foregroundStyle(.secondary)
                                SourceLink("项目来源", url: dictionary.sourceUrl)
                            }
                            .padding(.vertical, 4)
                        }
                    }
                    Section {
                        Button("在访达中显示词库文件夹") { revealFolder(list.folder) }
                    } footer: {
                        Note("词库安装脚本需要 PowerShell 7 和 .NET 10 SDK，例如：pwsh ./scripts/Install-Dictionary.ps1 -AppDirectory \"$HOME/Library/Application Support/LeXi\"。安装后重新打开 LeXi。Wiktionary 与 AI 属于在线补充，可在“查询与模型”中配置。")
                    }
                }
            }
        }
        .formStyle(.grouped)
        .task { await store.refreshDictionaries() }
    }

    private func revealFolder(_ path: String) {
        try? FileManager.default.createDirectory(atPath: path, withIntermediateDirectories: true)
        NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: path)])
    }
}

struct ConnectionSettings: View {
    @Bindable var store: LexiStore
    @State private var codexPath = ""

    var body: some View {
        Form {
            Section {
                Note("在官方登录页完成 OAuth 授权，LeXi 使用同一台电脑上的登录状态。主要模型、第二模型和义项判断共用对应账号。登录会即时生效，并与官方客户端共用账号。")
            }
            ProviderRow(store: store, provider: "codex", title: "ChatGPT · Codex", install: "https://learn.chatgpt.com/docs/cli")
            Section {
                HStack {
                    TextField("Codex 程序路径（留空自动检测）", text: $codexPath)
                        .onSubmit { Task { await store.update(["codexPath": .string(codexPath.trimmingCharacters(in: .whitespaces))]) } }
                    Button("选择程序…") { choose() }
                }
            }
            ProviderRow(store: store, provider: "claude", title: "Claude", install: "https://code.claude.com/docs/en/setup")
        }
        .formStyle(.grouped)
        .onAppear { codexPath = store.settings?.settings.codexPath ?? "" }
    }

    private func choose() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = false; panel.allowsMultipleSelection = false; panel.message = "选择 Codex 程序"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        codexPath = url.path
        Task { await store.update(["codexPath": .string(url.path)]) }
    }
}

struct ProviderRow: View {
    @Bindable var store: LexiStore
    let provider: String
    let title: String
    let install: String
    @State private var status = "等待检查登录状态"
    @State private var installed = false
    @State private var busy = false

    var body: some View {
        Section(title) {
            Text(status).foregroundStyle(.secondary).accessibilityAddTraits(.updatesFrequently)
            HStack {
                Button("登录 / 重新连接") { Task { await login() } }.disabled(busy || !installed)
                    .accessibilityLabel(provider == "codex" ? "登录 ChatGPT 订阅" : "登录 Claude 订阅")
                Button("检查状态") { Task { await refresh() } }.disabled(busy)
                SourceLink("安装说明", url: install)
            }
        }
        .task { await refresh() }
    }

    private func refresh() async {
        busy = true; status = "正在检查…"
        if let result = await store.connectionStatus(provider) { status = result.message; installed = result.installed }
        else { status = "无法确认登录状态，请稍后重试" }
        busy = false
    }

    // The user explicitly starts sign-in. The official client runs in Terminal and owns the
    // browser flow; LeXi never sees tokens or one-time codes.
    private func login() async {
        guard let command = await store.loginCommand(provider) else { status = "请先安装官方客户端，或检查程序路径"; return }
        do {
            try TerminalLauncher.open(command, name: "LeXi-login-\(provider)")
            status = "已打开官方登录，请在终端与浏览器中完成授权，然后点击“检查状态”"
        } catch {
            status = "无法打开官方登录，请检查客户端安装或程序路径"
        }
    }
}

struct SpeechSettings: View {
    var body: some View {
        Form {
            Section {
                Note("点击词条旁的扬声器朗读当前输入，再次点击停止。macOS 使用系统声线，按文字语言自动选择中文或英文声线，不需要 API Key。可在“系统设置 → 辅助功能 → 朗读内容”中下载更多声线。")
                Note("OpenAI、Grok 与 ElevenLabs 在线语音目前仅 Windows 版提供，macOS 版计划使用钥匙串保存密钥。")
            }
        }
        .formStyle(.grouped)
    }
}

struct Note: View {
    let text: String
    init(_ text: String) { self.text = text }
    var body: some View { Text(text).font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true) }
}

enum TerminalLauncher {
    // Writes a one-off .command script and opens it with Terminal: no Automation permission, and
    // the official client gets a real terminal for its interactive login.
    static func open(_ command: LoginCommand, name: String) throws {
        func quoted(_ value: String) -> String { "'" + value.replacingOccurrences(of: "'", with: "'\\''") + "'" }
        var script = "#!/bin/zsh\n"
        if !command.path.isEmpty { script += "export PATH=\(quoted(command.path))\n" }
        for variable in command.clearEnvironment where variable.allSatisfy({ $0.isLetter || $0.isNumber || $0 == "_" }) { script += "unset \(variable)\n" }
        script += "exec " + ([command.fileName] + command.arguments).map(quoted).joined(separator: " ") + "\n"
        let folder = FileManager.default.temporaryDirectory.appending(path: "LeXi", directoryHint: .isDirectory)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let file = folder.appending(path: name + ".command")
        try script.write(to: file, atomically: true, encoding: .utf8)
        try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: file.path)
        guard let terminal = NSWorkspace.shared.urlForApplication(withBundleIdentifier: "com.apple.Terminal") else { throw CocoaError(.fileNoSuchFile) }
        NSWorkspace.shared.open([file], withApplicationAt: terminal, configuration: NSWorkspace.OpenConfiguration())
    }
}
