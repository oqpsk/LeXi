import LeXiKit
import SwiftUI

// Recent explicit submissions, as one grouped list. Deleting a row never deletes cached definitions.
struct HistoryView: View {
    @Bindable var store: LexiStore
    let open: (HistoryEntry) -> Void
    @State private var filter: String

    init(store: LexiStore, open: @escaping (HistoryEntry) -> Void) {
        self.store = store; self.open = open
        // Coming back home keeps the search that is still applied to the list.
        _filter = State(initialValue: store.historyFilter)
    }

    var body: some View {
        let searching = !filter.isEmpty || !store.historyFilter.isEmpty
        VStack(alignment: .leading, spacing: 10) {
            if !store.history.error.isEmpty || store.history.entries.isEmpty && !searching {
                ContentUnavailableView {
                    Label("还没有查询历史", systemImage: "clock")
                } description: {
                    Text(store.history.error.isEmpty ? "查过的词语和文本会显示在这里。" : store.history.error)
                }
                .padding(.top, 40)
            } else {
                // The query capsule above looks words up; this smaller field only filters the list.
                HStack(alignment: .firstTextBaseline) {
                    Text("最近查询").font(.headline)
                    Spacer(minLength: 12)
                    SearchField(text: $filter)
                }
                if store.history.entries.isEmpty {
                    Text("没有匹配的历史。").foregroundStyle(.secondary).padding(.vertical, 8)
                } else {
                    VStack(spacing: 0) {
                        ForEach(Array(store.history.entries.enumerated()), id: \.element.id) { index, entry in
                            if index > 0 { Divider().padding(.leading, 14) }
                            Row(entry: entry, open: open, remove: { Task { await store.remove(entry) } })
                        }
                    }
                    .padding(.vertical, 4)
                    .contentGroup()
                    if store.history.entries.count < (store.history.total ?? store.history.entries.count) {
                        Button("更多历史") { Task { await store.moreHistory() } }
                    }
                }
            }
        }
        .task(id: filter) {
            guard filter != store.historyFilter else { return }
            try? await Task.sleep(for: .milliseconds(200)); guard !Task.isCancelled else { return }
            await store.searchHistory(filter)
        }
    }

    private struct SearchField: View {
        @Binding var text: String
        var body: some View {
            HStack(spacing: 5) {
                Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
                TextField("搜索历史", text: $text).textFieldStyle(.plain)
                if !text.isEmpty {
                    Button { text = "" } label: { Image(systemName: "xmark.circle.fill") }
                        .buttonStyle(.borderless).foregroundStyle(.tertiary)
                        .help("清除搜索").accessibilityLabel("清除搜索")
                }
            }
            .font(.callout)
            .padding(.horizontal, 10).padding(.vertical, 5)
            .frame(width: 200)
            .background(.quaternary.opacity(0.6), in: .capsule)
            .accessibilityElement(children: .contain)
            .accessibilityLabel("搜索查询历史")
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
