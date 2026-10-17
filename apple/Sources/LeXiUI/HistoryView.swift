import LeXiKit
import SwiftUI

// Recent explicit submissions. Deleting a row never deletes cached definitions.
struct HistoryView: View {
    @Bindable var store: LexiStore
    let open: (HistoryEntry) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            if !store.history.error.isEmpty || store.history.entries.isEmpty {
                Text(store.history.error.isEmpty ? "暂无查询历史。查过的词语和文本会显示在这里。" : store.history.error)
                    .foregroundStyle(.secondary)
                    .padding(.vertical, 8)
            }
            ForEach(store.history.entries) { entry in
                HStack(spacing: 6) {
                    Button { open(entry) } label: {
                        HStack {
                            Text(entry.preview).font(.system(size: 16)).lineLimit(1).truncationMode(.tail)
                            Spacer(minLength: 12)
                            Text(entry.kind).font(.caption).foregroundStyle(.secondary)
                        }
                        .padding(.horizontal, 12).padding(.vertical, 9)
                        .contentShape(.rect)
                    }
                    .buttonStyle(.plain)
                    .background(.quaternary.opacity(0.5), in: .rect(cornerRadius: 8))
                    .help(entry.query)
                    .accessibilityLabel("重新查询：" + entry.preview)
                    Button { Task { await store.remove(entry) } } label: { Image(systemName: "xmark") }
                        .buttonStyle(.borderless)
                        .help("删除这条历史")
                        .accessibilityLabel("删除历史：" + entry.preview)
                }
            }
        }
    }
}
