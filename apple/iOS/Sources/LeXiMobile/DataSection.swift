import LeXiKit
import SwiftUI
import UniformTypeIdentifiers

// Backups and caches, as on the Mac. A backup is written inside the app and then saved where the
// reader chooses in Files; an imported one is copied in from Files before Core reads it.
struct DataSection: View {
    @Bindable var store: LexiStore
    @State private var usage: [CacheUsage] = []
    @State private var includeSettings = false
    @State private var message = ""
    @State private var busy = false
    @State private var clearing: CacheUsage?
    @State private var exported: URL?
    @State private var importing = false
    @State private var pending: (url: URL, preview: BackupPreview)?

    var body: some View {
        Form {
            Section {
                Button("导出备份…") { export() }
                Button("导入备份…") { importing = true }
                Toggle("导入时同时恢复设置", isOn: $includeSettings)
            } header: {
                Text("备份与迁移")
            } footer: {
                Text("备份包含设置、历史和已保存查询，可能含私人文本；可存到“文件”或 iCloud 云盘，在 Mac 上导入。密钥、语音和词库文件不会导出，新设备需重新填写密钥。")
            }
            Section {
                ForEach(usage) { item in
                    HStack {
                        VStack(alignment: .leading, spacing: 2) {
                            Text(item.name)
                            Text("\(item.files) 个文件 · " + Self.size(item.bytes)).font(.caption).foregroundStyle(.secondary)
                        }
                        Spacer()
                        Button("清理…", role: .destructive) { clearing = item }.buttonStyle(.borderless)
                    }
                }
            } header: {
                Text("缓存")
            } footer: {
                Text("清理中间缓存保留历史与已保存查询。语音缓存删除后再次播放可能重新计费；删除已保存查询会让下次查询重新读取资料。")
            }
            if !message.isEmpty { Section { Text(message).foregroundStyle(.secondary) } }
        }
        .disabled(busy || store.view.progress?.busy == true)
        .task { await refresh() }
        .confirmationDialog("清理缓存？此操作无法撤销。", isPresented: Binding(get: { clearing != nil }, set: { if !$0 { clearing = nil } }), titleVisibility: .visible) {
            if let item = clearing {
                Button("清理“" + item.name + "”", role: .destructive) { Task { await run { try await store.clearCache(item.id) }; clearing = nil } }
            }
        }
        .fileMover(isPresented: Binding(get: { exported != nil }, set: { if !$0 { discardExport() } }), file: exported) { result in
            exported = nil
            message = (try? result.get()) != nil ? "备份已保存。" : "备份未保存。"
        }
        .fileImporter(isPresented: $importing, allowedContentTypes: [.data]) { result in
            if case .success(let url) = result { inspect(url) }
        }
        .alert("导入 LeXi 备份", isPresented: Binding(get: { pending != nil }, set: { if !$0 { discardImport() } }), presenting: pending) { item in
            Button("导入") { Task { await importBackup(item.url) } }
            Button("取消", role: .cancel) { discardImport() }
        } message: { item in
            Text("将合入 \(item.preview.history) 条历史和 \(item.preview.results) 份查询；已有查询会保留。" + (includeSettings ? "设置会恢复，密钥需重新填写。" : "当前设置保持。"))
        }
    }

    static func size(_ bytes: Int64) -> String {
        if bytes == 0 { return "0 KB" }
        if bytes < 1000 { return "不到 1 KB" }
        let formatter = ByteCountFormatter()
        formatter.countStyle = .file
        formatter.allowedUnits = [.useKB, .useMB, .useGB]
        return formatter.string(fromByteCount: bytes)
    }

    private func refresh() async { do { usage = try await store.dataUsage() } catch { message = "暂时无法读取缓存。" } }
    private func run(_ action: () async throws -> Void) async {
        busy = true; defer { busy = false }
        do { try await action(); message = "操作完成。"; await refresh() }
        catch { message = "操作未完成：" + error.localizedDescription }
    }

    // A dated file in the app's temporary folder; Files then moves it where the reader chooses.
    private func export() {
        let stamp = Date.now.formatted(.iso8601.year().month().day())
        let url = FileManager.default.temporaryDirectory.appending(path: "LeXi-backup-\(stamp).lexibackup")
        try? FileManager.default.removeItem(at: url)
        Task {
            busy = true; defer { busy = false }
            do { try await store.exportBackup(url.path); exported = url }
            catch { message = "导出未完成：" + error.localizedDescription }
        }
    }
    private func discardExport() { if let exported { try? FileManager.default.removeItem(at: exported) }; exported = nil }

    // The picked file is outside the app; a copy inside it is what Core reads.
    private func inspect(_ picked: URL) {
        let copy = FileManager.default.temporaryDirectory.appending(path: "LeXi-import-\(UUID().uuidString).lexibackup")
        let access = picked.startAccessingSecurityScopedResource()
        defer { if access { picked.stopAccessingSecurityScopedResource() } }
        do { try FileManager.default.copyItem(at: picked, to: copy) }
        catch { message = "无法读取所选文件。"; return }
        Task {
            busy = true; defer { busy = false }
            do { pending = (copy, try await store.inspectBackup(copy.path)) }
            catch { try? FileManager.default.removeItem(at: copy); message = "导入未完成：" + error.localizedDescription }
        }
    }
    private func importBackup(_ url: URL) async {
        pending = nil
        await run { try await store.importBackup(url.path, includeSettings: includeSettings) }
        try? FileManager.default.removeItem(at: url)
    }
    private func discardImport() { if let pending { try? FileManager.default.removeItem(at: pending.url) }; pending = nil }
}
