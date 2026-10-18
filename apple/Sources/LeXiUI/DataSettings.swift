import AppKit
import LeXiKit
import SwiftUI
import UniformTypeIdentifiers

struct DataSettings: View {
    @Bindable var store: LexiStore
    @State private var usage: [CacheUsage] = []
    @State private var includeSettings = false
    @State private var message = ""
    @State private var busy = false
    @State private var clearing: CacheUsage?
    var body: some View {
        Form {
            Section("备份与迁移") {
                HStack {
                    Button("导出备份…") { export() }
                    Button("导入备份…") { restore() }
                }
                Toggle("导入时同时恢复设置", isOn: $includeSettings)
                Text("备份包含设置、历史和已保存查询，可能含私人文本。密钥、OAuth 登录、语音和词库文件不会导出；新设备需重新连接账号。")
                    .font(.callout).foregroundStyle(.secondary)
            }
            Section("缓存") {
                ForEach(usage) { item in
                    HStack {
                        VStack(alignment: .leading) {
                            Text(item.name)
                            Text("\(item.files) 个文件 · " + ByteCountFormatter.string(fromByteCount: item.bytes, countStyle: .file)).font(.callout).foregroundStyle(.secondary)
                        }
                        Spacer()
                        Button("清理…", role: .destructive) { clearing = item }
                    }
                }
                Text("清理中间缓存保留历史与已保存查询。语音缓存删除后再次播放可能重新计费；删除已保存查询会让下次查询重新读取资料。")
                    .font(.callout).foregroundStyle(.secondary)
            }
            if !message.isEmpty { Text(message).foregroundStyle(.secondary) }
        }
        .formStyle(.grouped).disabled(busy || store.view.progress?.busy == true)
        .task { await refresh() }
        .confirmationDialog("清理缓存？此操作无法撤销。", isPresented: Binding(get: { clearing != nil }, set: { if !$0 { clearing = nil } })) {
            if let item = clearing { Button("清理“" + item.name + "”", role: .destructive) { Task { await run { try await store.clearCache(item.id) }; clearing = nil } } }
        }
    }
    private func refresh() async { do { usage = try await store.dataUsage() } catch { message = "暂时无法读取缓存。" } }
    private func run(_ action: () async throws -> Void) async {
        busy = true; defer { busy = false }
        do { try await action(); message = "操作完成。"; await refresh() }
        catch { message = "操作未完成：" + error.localizedDescription }
    }
    private func export() {
        let panel = NSSavePanel(); panel.nameFieldStringValue = "LeXi-backup.lexibackup"; panel.allowedContentTypes = [.data]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        Task { await run { try await store.exportBackup(url.path) } }
    }
    private func restore() {
        let panel = NSOpenPanel(); panel.allowsMultipleSelection = false
        guard panel.runModal() == .OK, let url = panel.url else { return }
        Task {
            await run {
                let preview = try await store.inspectBackup(url.path)
                let alert = NSAlert(); alert.messageText = "导入 LeXi 备份"
                alert.informativeText = "将合入 \(preview.history) 条历史和 \(preview.results) 份查询；已有查询会保留。" + (includeSettings ? "设置会恢复，密钥需重新填写。" : "当前设置保持。")
                alert.addButton(withTitle: "导入"); alert.addButton(withTitle: "取消")
                guard alert.runModal() == .alertFirstButtonReturn else { return }
                try await store.importBackup(url.path, includeSettings: includeSettings)
            }
        }
    }
}
