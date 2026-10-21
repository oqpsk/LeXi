import LeXiKit
import LeXiShared
import SwiftUI

// The one screen. The Liquid Glass query capsule sits at the top, where the hand typing below never
// covers it, with suggestions under it; history fills the page before a query. The status line is
// the footer, as on the Mac. Swiping in from an edge goes back or forward, as in Safari.
struct RootView: View {
    @Bindable var store: LexiStore
    var speech: SpeechController
    @State private var text = ""
    @State private var previous = ""
    @State private var programmatic = false
    @State private var showSettings = false
    @FocusState private var searching: Bool
    // An edge swipe in progress: positive from the left edge (back), negative from the right (forward).
    @State private var swipe: CGFloat = 0
    private static let edge: CGFloat = 20, threshold: CGFloat = 90

    var body: some View {
        NavigationStack {
            ScrollView {
                content
                    .padding(.horizontal, 18).padding(.top, 4).padding(.bottom, 24)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            .scrollDismissesKeyboard(.interactively)
            .background(Color(uiColor: .systemGroupedBackground))
            // A strip along each edge takes the swipe, so a row under the finger is not tapped as well.
            .overlay(alignment: .leading) { ZStack(alignment: .leading) { edgeStrip(forward: false); swipeMark(forward: false) } }
            .overlay(alignment: .trailing) { ZStack(alignment: .trailing) { edgeStrip(forward: true); swipeMark(forward: true) } }
            .sensoryFeedback(.selection, trigger: abs(swipe) > Self.threshold) { _, armed in armed }
            // In the safe area, so the list hangs just below the query capsule.
            .overlay(alignment: .top) { suggestions }
            .safeAreaBar(edge: .top) { searchBar }
            .safeAreaBar(edge: .bottom) { statusBar }
            .navigationTitle(store.view.kind == .home ? "LeXi" : "")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { toolbar }
            .sheet(isPresented: $showSettings) { SettingsView(store: store, speech: speech) }
        }
        .onChange(of: store.view.request) { _, _ in pageChanged() }
        .onChange(of: store.view.status) { _, _ in speech.clearStatus() }
        .onChange(of: searching) { _, active in if !active { store.clearInputPreview() } }
        // The lookup is ready the moment LeXi opens, as on the desktop. The field may join the
        // hierarchy a moment after the first appearance, so focus is asked for again on the next turn.
        .defaultFocus($searching, true)
        .task {
            await Task.yield()
            // For UI checks and screenshots: `-LexiSettings YES` opens settings at launch.
            if UserDefaults.standard.bool(forKey: "LexiSettings") { showSettings = true } else { searching = true }
        }
    }

    @ViewBuilder private var content: some View {
        switch store.phase {
        case .failed(let message):
            ContentUnavailableView("LeXi 无法查询", systemImage: "exclamationmark.triangle", description: Text(message))
        case .starting:
            ProgressView().frame(maxWidth: .infinity).padding(.top, 80)
        case .ready:
            if store.view.kind == .home {
                HistoryView(store: store) { entry in setText(entry.query); submit(entry.query) }
            } else {
                EntryView(store: store, speech: speech)
            }
        }
    }

    private var online: Bool { store.settings?.settings.online ?? true }
    private var canRegenerate: Bool { store.view.kind != .home && store.view.canRegenerate && online }
    private var status: String { speech.status.isEmpty ? store.view.status : speech.status }

    // The Mac footer: the soft matrix beside the status; nothing spins in the query capsule.
    @ViewBuilder private var statusBar: some View {
        if store.view.kind != .home && !status.isEmpty {
            HStack(spacing: 8) {
                QueryActivityMark(activity: store.view.progress?.activity ?? .idle, size: 14).foregroundStyle(.secondary)
                Text(status).font(.footnote).foregroundStyle(.secondary).lineLimit(1)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 22).padding(.vertical, 6)
            .accessibilityElement(children: .combine)
            .accessibilityLabel("查询状态：" + status)
        }
    }

    private var searchBar: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 10) {
                Image(systemName: "magnifyingglass").foregroundStyle(.secondary).accessibilityHidden(true)
                TextField("查词或翻译", text: $text, axis: .vertical)
                    .lineLimit(1...4)
                    .font(.system(size: 18))
                    .focused($searching)
                    .submitLabel(.search)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                    .onChange(of: text) { old, value in edited(old, value) }
                    .accessibilityLabel("查询")
                if !text.isEmpty {
                    Button { clear() } label: { Image(systemName: "xmark.circle.fill").foregroundStyle(.tertiary) }
                        .buttonStyle(.plain)
                        .accessibilityLabel("清除")
                }
            }
            .padding(.horizontal, 18).padding(.vertical, 13)
            .glassEffect(.regular.interactive(), in: .rect(cornerRadius: 26, style: .continuous))
        }
        .padding(.horizontal, 14).padding(.top, 2).padding(.bottom, 6)
    }

    // The preview of what is being typed sits below the suggestions, so the list a finger is
    // heading for never moves when the preview arrives.
    @ViewBuilder private var suggestions: some View {
        if searching && (!store.suggestions.isEmpty || store.inputPreview != nil) {
            VStack(spacing: 8) {
                if !store.suggestions.isEmpty { suggestionList }
                if let preview = store.inputPreview {
                    Button { setText(preview.query); submit(preview.query) } label: { InputPreviewCard(preview: preview) }
                        .buttonStyle(.plain)
                }
            }
            .padding(.horizontal, 14).padding(.top, 6)
        }
    }

    // Back from the left edge and forward from the right, once the drag is mostly sideways and
    // only when there is a page that way. Past the threshold a light tap confirms it.
    @ViewBuilder private func edgeStrip(forward: Bool) -> some View {
        let navigation = store.view.navigation
        if forward ? navigation.canForward : navigation.canBack {
            Color.clear.frame(width: Self.edge).contentShape(.rect)
                .gesture(DragGesture(minimumDistance: 8)
                    .onChanged { value in
                        let dx = value.translation.width
                        guard abs(dx) > abs(value.translation.height) * 1.5 else { return }
                        swipe = forward ? min(dx, 0) : max(dx, 0)
                    }
                    .onEnded { _ in
                        let travelled = swipe
                        withAnimation(.snappy(duration: 0.25)) { swipe = 0 }
                        if travelled > Self.threshold { navigate(-1) } else if travelled < -Self.threshold { navigate(1) }
                    })
                .accessibilityHidden(true)
        }
    }

    // A glass chevron that follows the finger in from the edge and fills once releasing would turn the page.
    @ViewBuilder private func swipeMark(forward: Bool) -> some View {
        let distance = forward ? -swipe : swipe
        if distance > 0 {
            let progress = min(distance / Self.threshold, 1)
            Image(systemName: forward ? "chevron.forward" : "chevron.backward")
                .font(.system(size: 18, weight: .semibold))
                .foregroundStyle(progress >= 1 ? AnyShapeStyle(.primary) : AnyShapeStyle(.secondary))
                .frame(width: 44, height: 44)
                .glassEffect(.regular, in: .circle)
                .scaleEffect(0.7 + 0.3 * progress)
                .opacity(Double(progress))
                .offset(x: (forward ? -1 : 1) * (min(distance, Self.threshold + 24) * 0.6 - 28))
                .allowsHitTesting(false)
                .accessibilityHidden(true)
        }
    }

    private var suggestionList: some View {
            VStack(alignment: .leading, spacing: 0) {
                ForEach(Array(store.suggestions.prefix(6).enumerated()), id: \.offset) { _, word in
                    Button { setText(word); submit(word) } label: {
                        Text(word).font(LexiTheme.lexical(18))
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(.horizontal, 18).padding(.vertical, 11)
                            .contentShape(.rect)
                    }
                    .buttonStyle(.plain)
                }
            }
            .padding(.vertical, 6)
            .glassEffect(.regular, in: .rect(cornerRadius: 22, style: .continuous))
            .accessibilityElement(children: .contain)
            .accessibilityLabel("输入建议")
    }

    @ToolbarContentBuilder private var toolbar: some ToolbarContent {
        let navigation = store.view.navigation
        ToolbarItemGroup(placement: .topBarLeading) {
            Button { navigate(-1) } label: { Image(systemName: "chevron.backward") }
                .disabled(!navigation.canBack).accessibilityLabel("后退")
            Button { navigate(1) } label: { Image(systemName: "chevron.forward") }
                .disabled(!navigation.canForward).accessibilityLabel("前进")
        }
        ToolbarItemGroup(placement: .topBarTrailing) {
            Menu {
                if store.view.kind != .home {
                    if store.view.canRetry == true {
                        Button { speech.stop(); Task { await store.retryFailed() } } label: { Label("重试未完成", systemImage: "arrow.clockwise.circle") }
                    }
                    Button { regenerate() } label: { Label("重新生成", systemImage: "arrow.clockwise") }.disabled(!canRegenerate)
                }
                Button { Task { await store.setOnline(!online) } } label: {
                    Label(store.settings?.controls.onlineLabel ?? "在线补充", systemImage: online ? "globe" : "network.slash")
                }
                Button { goHome() } label: { Label("查询历史", systemImage: "clock") }
            } label: { Image(systemName: "ellipsis") }
            .accessibilityLabel("更多操作")
            Button { showSettings = true } label: { Image(systemName: "gearshape") }.accessibilityLabel("设置")
        }
    }

    private func setText(_ value: String) { if text != value { programmatic = true; text = value } }

    // Return on the keyboard ends the query; pasted line breaks stay, for translation.
    private func edited(_ old: String, _ value: String) {
        if programmatic { programmatic = false; return }
        if value.hasSuffix("\n") && String(value.dropLast()) == old {
            setText(old); submit(old); return
        }
        if value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            store.clearSuggestions(); store.clearInputPreview()
            if store.view.kind != .home { Task { await store.home() } }
            return
        }
        Task { await store.suggest(value) }
        store.previewInput(value)
    }

    private func submit(_ value: String) {
        guard !value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return }
        speech.stop()
        store.clearSuggestions(); store.clearInputPreview()
        searching = false
        Task { await store.submit(value) }
    }

    private func clear() {
        setText("")
        store.clearSuggestions(); store.clearInputPreview()
        Task { await store.home() }
        searching = true
    }

    private func regenerate() { guard canRegenerate else { return }; speech.stop(); Task { await store.regenerate() } }

    private func goHome() { speech.stop(); setText(""); Task { await store.home() } }

    private func navigate(_ direction: Int) {
        Task {
            guard let page = await store.navigate(direction) else { return }
            if page.query.isEmpty { setText("") }
        }
    }

    // A new page (from a link, history or back/forward) puts the keyboard away so the result shows.
    private func pageChanged() {
        speech.stop()
        if store.view.kind != .home { setText(store.view.input); searching = false }
    }
}

// Recent explicit submissions, searchable and loaded a page at a time, as on the Mac. Deleting a row
// never deletes cached definitions.
// Recent explicit submissions, loaded a page at a time. On a phone the list needs no search field of
// its own: people took one for the lookup field. Deleting a row never deletes cached definitions.
struct HistoryView: View {
    @Bindable var store: LexiStore
    let open: (HistoryEntry) -> Void

    var body: some View {
        Group {
            if !store.history.error.isEmpty || store.history.entries.isEmpty {
                ContentUnavailableView {
                    Label("查一个词", systemImage: "character.book.closed")
                } description: {
                    Text(store.history.error.isEmpty ? "在上方输入单词、短语或一段文字。查过的内容会留在这里。" : store.history.error)
                }
                .padding(.top, 40)
            } else {
                VStack(alignment: .leading, spacing: 10) {
                    Text("最近查询").font(.subheadline.weight(.semibold)).foregroundStyle(.secondary).padding(.horizontal, 4)
                    VStack(spacing: 0) {
                        ForEach(Array(store.history.entries.enumerated()), id: \.element.id) { index, entry in
                            if index > 0 { Divider().padding(.leading, 18) }
                            row(entry)
                                // The next page loads as the last row comes into view.
                                .onAppear { if index == store.history.entries.count - 1 && hasMore { Task { await store.moreHistory() } } }
                        }
                    }
                    .contentGroup()
                }
            }
        }
        // A filter left from an earlier version would otherwise hide entries with no way to clear it.
        .task { if !store.historyFilter.isEmpty { await store.searchHistory("") } }
    }

    private var hasMore: Bool { store.history.entries.count < (store.history.total ?? store.history.entries.count) }

    private func row(_ entry: HistoryEntry) -> some View {
        Button { open(entry) } label: {
            HStack(spacing: 12) {
                Text(entry.preview)
                    .font(entry.translation ? .system(size: 17) : LexiTheme.lexical(19))
                    .lineLimit(1).foregroundStyle(.primary)
                Spacer(minLength: 8)
                Text(entry.kind).font(.caption).foregroundStyle(.secondary)
            }
            .padding(.horizontal, 18).padding(.vertical, 13)
            .contentShape(.rect)
        }
        .buttonStyle(.plain)
        .accessibilityLabel("重新查询：" + entry.preview)
        .contextMenu {
            Button(role: .destructive) { Task { await store.remove(entry) } } label: { Label("从历史中删除", systemImage: "trash") }
        }
    }
}

// The TL;DR of what is being typed, as on the Mac: the saved result or a local dictionary's, shown
// after a short pause without going online. A tap opens the full page.
struct InputPreviewCard: View {
    let preview: InputPreview
    private var message: String { "\(preview.label)：\(preview.text)" }
    var body: some View {
        HStack(alignment: .center, spacing: 12) {
            QuickReadBadge()
            Text(verbatim: message).font(.system(size: 16, weight: .medium))
                .lineLimit(2).frame(maxWidth: .infinity, alignment: .leading)
            Image(systemName: "chevron.forward").font(.footnote.weight(.semibold)).foregroundStyle(.tertiary)
        }
        .padding(.horizontal, 16).padding(.vertical, 11)
        .glassEffect(.regular.tint(LexiTheme.pink.opacity(0.35)), in: .rect(cornerRadius: 22, style: .continuous))
        .accessibilityElement(children: .combine)
        .accessibilityLabel(Text(verbatim: "TL;DR · \(message)"))
        .accessibilityHint(Text(preview.source))
    }
}
