import AppKit
import LeXiKit
import SwiftUI

// macOS settings in the System Settings style. Changes apply immediately; Lexi.Core validates each
// one with the same rules and messages as the Windows dialog, and a rejected change leaves the
// saved settings intact. Controls use the system accent color.
public struct SettingsView: View {
    @Bindable var store: LexiStore
    public init(store: LexiStore) { self.store = store }

    public var body: some View {
        TabView {
            Tab("查询", systemImage: "text.magnifyingglass") { QuerySettings(store: store) }
            Tab("词典", systemImage: "books.vertical") { DictionarySettings(store: store) }
            Tab("账号", systemImage: "person.crop.circle") { ConnectionSettings(store: store) }
            Tab("发音", systemImage: "speaker.wave.2") { SpeechSettings() }
        }
        .frame(width: 600, height: 560)
        .task { await store.refreshSettings() }
    }
}

// A setting's name with a short secondary explanation underneath, as in System Settings.
struct SettingLabel: View {
    let title: String
    let detail: String
    init(_ title: String, _ detail: String) { self.title = title; self.detail = detail }
    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title)
            Text(detail).font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
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
                    toggle(SettingLabel("在线补充", "使用 Wiktionary 与模型补充释义；关闭后只查本地词库和已保存的结果。"), settings.online, "online")
                    toggle(SettingLabel("自动补充双语释义", "查词时自动请模型补充；关闭后可点“重新生成”手动获取。"), settings.autoAi, "autoAi")
                }
                Section {
                    modelPicker(SettingLabel("主要模型", "用于查词与文本翻译"), payload.models, selected: settings.provider + ":" + settings.model) { choice in
                        ["provider": .string(choice.provider), "model": .string(choice.id)]
                    }
                    toggle(SettingLabel("双模型同时查词", "两个模型独立查词，先返回的释义先显示"), settings.parallelLookup, "parallelLookup")
                    modelPicker(SettingLabel("第二个查询模型", "须与主要模型不同"), payload.models, selected: secondaryKey(settings)) { choice in
                        ["secondaryProvider": .string(choice.provider), "secondaryModel": .string(choice.id)]
                    }
                    .disabled(!settings.parallelLookup)
                } header: {
                    Text("查询模型")
                } footer: {
                    Note("使用本机 Codex 与 Claude Code 的订阅登录。查过的内容直接使用缓存；切换模型后，点“重新生成”才会更新已有结果。Claude 关闭额外思考，Codex 使用 Low + Fast，优先速度并消耗更多订阅额度。")
                }
                Section {
                    toggle(SettingLabel("自动整理同类义项", "合并重复的说法，包含单个模型内部"), settings.mergeSenses, "mergeSenses")
                    modelPicker(SettingLabel("义项判断模型", "使用 Codex 订阅"), payload.judgeModels, selected: "codex:" + settings.judgeModel) { choice in
                        ["judgeModel": .string(choice.id)]
                    }
                    .disabled(!settings.mergeSenses)
                } header: {
                    Text("义项整理")
                } footer: {
                    Note("同一词义的细分用法归为一组，优先展示涵盖更完整的原文；其他表述保留在“用法细分”中。不同含义和不确定的区别仍分开。")
                }
                if let error {
                    Section { Label(error, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.red) }
                }
                Section("键盘快捷键") {
                    ForEach(Self.shortcuts, id: \.0) { action, keys in
                        LabeledContent(action) { Text(keys).foregroundStyle(.secondary).monospaced() }
                    }
                }
            }
            .formStyle(.grouped)
        } else {
            ProgressView()
        }
    }

    private static let shortcuts = [("查询", "Return"), ("换行", "⇧Return"), ("收起建议", "Esc"), ("再查一个", "⌘L"),
        ("重新生成", "⌘R"), ("后退 / 前进", "⌘[  ⌘]"), ("查询历史", "⌘Y"), ("设置", "⌘,")]

    // The secondary model defaults to the other service's fast model, as in Lexi.Core.
    private func secondaryKey(_ settings: AppSettings) -> String {
        settings.secondaryProvider.isEmpty
            ? (settings.provider == "claude" ? "codex:gpt-6-luna" : "claude:haiku")
            : settings.secondaryProvider + ":" + settings.secondaryModel
    }

    private func toggle(_ label: SettingLabel, _ value: Bool, _ key: String) -> some View {
        Toggle(isOn: Binding(get: { value }, set: { apply([key: .bool($0)]) })) { label }
    }

    private func modelPicker(_ label: SettingLabel, _ choices: [ModelChoice], selected: String,
                             change: @escaping (ModelChoice) -> [String: SettingValue]) -> some View {
        Picker(selection: Binding(get: { selected }, set: { key in
            if let choice = choices.first(where: { $0.key == key }) { apply(change(choice)) }
        })) {
            ForEach(choices, id: \.key) { Text($0.label).tag($0.key) }
        } label: { label }
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
                    Toggle(isOn: Binding(get: { payload.settings.offlineEnabled },
                        set: { value in Task { await store.update(["offlineEnabled": .bool(value)]) } })) {
                        SettingLabel("离线查询", "启用的词典用于本地查询与输入建议")
                    }
                }
                if let list = store.dictionaries {
                    Section {
                        if list.dictionaries.isEmpty {
                            Text("尚未安装离线词典").foregroundStyle(.secondary)
                        }
                        ForEach(list.dictionaries) { dictionary in
                            Toggle(isOn: Binding(
                                get: { dictionary.available && !payload.settings.disabledDictionaries.contains(dictionary.id) },
                                set: { enabled in
                                    var disabled = payload.settings.disabledDictionaries.filter { $0 != dictionary.id }
                                    if !enabled { disabled.append(dictionary.id) }
                                    Task { await store.update(["disabledDictionaries": .strings(disabled)]) }
                                })) {
                                VStack(alignment: .leading, spacing: 3) {
                                    Text(dictionary.name)
                                    Text("\(dictionary.languages) · \(dictionary.entryCount.formatted()) 个检索词条 · \(ByteCountFormatter.string(fromByteCount: dictionary.bytes, countStyle: .file))")
                                        .font(.callout).foregroundStyle(.secondary)
                                    HStack(spacing: 8) {
                                        Text("\(dictionary.attribution) · \(dictionary.version.isEmpty ? "" : "版本 \(dictionary.version) · ")\(dictionary.license)")
                                            .font(.caption).foregroundStyle(.tertiary)
                                        SourceLink("来源", url: dictionary.sourceUrl).font(.caption)
                                    }
                                }
                            }
                            .disabled(!dictionary.available)
                        }
                    } header: {
                        Text("已安装 \(list.dictionaries.filter(\.available).count) 部词典")
                    } footer: {
                        Note("取消勾选即可停用，词库文件会保留。安装词库需要 PowerShell 7 和 .NET 10 SDK，例如 pwsh ./scripts/Install-Dictionary.ps1 -AppDirectory \"$HOME/Library/Application Support/LeXi\"，安装后重新打开 LeXi。")
                    }
                    Section {
                        Button("在访达中显示词库文件夹") { revealFolder(list.folder) }
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
                ProviderRow(store: store, provider: "codex", title: "ChatGPT · Codex", symbol: "sparkles", install: "https://learn.chatgpt.com/docs/cli")
                LabeledContent {
                    HStack {
                        TextField("自动检测", text: $codexPath)
                            .labelsHidden()
                            .onSubmit { Task { await store.update(["codexPath": .string(codexPath.trimmingCharacters(in: .whitespaces))]) } }
                        Button("选择…") { choose() }
                    }
                } label: { SettingLabel("程序路径", "留空时自动查找命令行与 ChatGPT 应用中的 Codex") }
            }
            Section {
                ProviderRow(store: store, provider: "claude", title: "Claude", symbol: "asterisk", install: "https://code.claude.com/docs/en/setup")
            } footer: {
                Note("在官方登录页完成授权，LeXi 使用同一台电脑上的登录状态，不读取令牌。登录会即时生效并与官方客户端共用账号；主要模型、第二模型和义项判断共用对应账号。")
            }
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
    let symbol: String
    let install: String
    @State private var status = "正在检查…"
    @State private var state: LoginStatus?
    @State private var busy = false

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: symbol).font(.title2).foregroundStyle(.secondary).frame(width: 30)
            VStack(alignment: .leading, spacing: 3) {
                Text(title).font(.headline)
                HStack(spacing: 6) {
                    Circle().fill(dot).frame(width: 7, height: 7)
                    Text(status).font(.callout).foregroundStyle(.secondary).accessibilityAddTraits(.updatesFrequently)
                }
            }
            Spacer()
            if state?.installed == false {
                SourceLink("安装说明", url: install)
            } else {
                Button("检查") { Task { await refresh() } }.disabled(busy)
                Button(state?.signedIn == true ? "重新登录…" : "登录…") { Task { await login() } }
                    .disabled(busy || state?.installed != true)
                    .accessibilityLabel(provider == "codex" ? "登录 ChatGPT 订阅" : "登录 Claude 订阅")
            }
        }
        .padding(.vertical, 4)
        .task { await refresh() }
    }

    private var dot: Color {
        guard let state else { return .secondary }
        return state.signedIn ? .green : state.installed ? .orange : .secondary
    }

    private func refresh() async {
        busy = true; status = "正在检查…"
        if let result = await store.connectionStatus(provider) { status = result.message; state = result }
        else { status = "无法确认登录状态，请稍后重试" }
        busy = false
    }

    // The user explicitly starts sign-in. The official client runs in Terminal and owns the
    // browser flow; LeXi never sees tokens or one-time codes.
    private func login() async {
        guard let command = await store.loginCommand(provider) else { status = "请先安装官方客户端，或检查程序路径"; return }
        do {
            try TerminalLauncher.open(command, name: "LeXi-login-\(provider)")
            status = "已在“终端”中打开官方登录，完成后点“检查”"
        } catch {
            status = "无法打开官方登录，请检查客户端安装或程序路径"
        }
    }
}

struct SpeechSettings: View {
    var body: some View {
        Form {
            Section {
                LabeledContent {
                    Text("按文字语言自动选择").foregroundStyle(.secondary)
                } label: { SettingLabel("系统声线", "点击词条旁的扬声器朗读，再次点击停止") }
            } footer: {
                Note("可在“系统设置 → 辅助功能 → 朗读内容”中下载更多声线。OpenAI、Grok 与 ElevenLabs 在线语音目前仅 Windows 版提供。")
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
