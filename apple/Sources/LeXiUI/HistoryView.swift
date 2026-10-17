import LeXiKit
import SwiftUI

// Recent explicit submissions, as one grouped list. Deleting a row never deletes cached definitions.
struct HistoryView: View {
    @Bindable var store: LexiStore
    let open: (HistoryEntry) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            if !store.history.error.isEmpty || store.history.entries.isEmpty {
                ContentUnavailableView {
                    Label("还没有查询历史", systemImage: "clock")
                } description: {
                    Text(store.history.error.isEmpty ? "查过的词语和文本会显示在这里。" : store.history.error)
                }
                .padding(.top, 40)
            } else {
                Text("最近查询").font(.headline)
                VStack(spacing: 0) {
                    ForEach(Array(store.history.entries.enumerated()), id: \.element.id) { index, entry in
                        if index > 0 { Divider().padding(.leading, 14) }
                        Row(entry: entry, open: open, remove: { Task { await store.remove(entry) } })
                    }
                }
                .padding(.vertical, 4)
                .contentGroup()
            }
        }
    }

    private struct Row: View {
        let entry: HistoryEntry
        let open: (HistoryEntry) -> Void
        let remove: () -> Void
        @State private var hovering = false

        var body: some View {
            HStack(spacing: 8) {
                Button { open(entry) } label: {
                    HStack {
                        Image(systemName: entry.translation ? "text.bubble" : "character.book.closed")
                            .foregroundStyle(.secondary).frame(width: 20)
                        Text(entry.preview).font(.system(size: 15)).lineLimit(1).truncationMode(.tail)
                        Spacer(minLength: 12)
                        Text(entry.kind).font(.caption).foregroundStyle(.secondary)
                    }
                    .padding(.leading, 14).padding(.vertical, 10)
                    .contentShape(.rect)
                }
                .buttonStyle(.plain)
                .help(entry.query)
                .accessibilityLabel("重新查询：" + entry.preview)
                Button(action: remove) { Image(systemName: "xmark.circle.fill").frame(width: 28, height: 28).contentShape(.rect) }
                    .buttonStyle(.borderless)
                    .foregroundStyle(.tertiary)
                    .opacity(hovering ? 1 : 0.55)
                    .padding(.trailing, 8)
                    .help("删除这条历史")
                    .accessibilityLabel("删除历史：" + entry.preview)
            }
            .onHover { hovering = $0 }
            .contextMenu { Button("删除这条历史", role: .destructive, action: remove) }
        }
    }
}
