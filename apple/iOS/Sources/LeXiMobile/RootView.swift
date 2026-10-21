import LeXiKit
import LeXiShared
import SwiftUI

// The one screen. The Liquid Glass query capsule sits at the top, where the hand typing below never
// covers it, with suggestions under it; history fills the page before a query. The status line is
// the footer, as on the Mac. Back and forward slide the page the iOS way: it follows the finger in
// from the edge over the page beneath, which Safari mirrors for forward; the bars stay put.
struct RootView: View {
    @Bindable var store: LexiStore
    var speech: SpeechController
    @State private var text = ""
    @State private var previous = ""
    @State private var programmatic = false
    @State private var showSettings = false
    @FocusState private var searching: Bool
    // Each page as last drawn, by Core's page id, so a slide shows the page beneath or ahead as it was.
    @State private var drawn: [Int64: QueryView] = [:]
    // How far the current page has slid: positive towards the page behind, negative towards the one ahead.
    @State private var slide: CGFloat = 0
    @State private var width: CGFloat = 400
    // The page a finished slide moved to, drawn from memory until Core's view for it arrives.
    @State private var arriving: Int64?
    // The request and page of the last view taken as a page's own.
    @State private var shown: (request: Int64, page: Int64) = (-1, -1)
    // The system's side margin, which the toolbar's glass buttons keep: 16 pt, 20 on the larger iPhones.
    @State private var margin: CGFloat = 16
    private static let edge: CGFloat = 20

    var body: some View {
        NavigationStack {
            ZStack {
                if slide > 0, let id = target(back: true) {
                    // The page behind waits a little to the left and dimmed, as under a navigation pop.
                    snapshot(drawn[id])
                        .overlay { Color.black.opacity(0.08 * (1 - slide / max(width, 1))).allowsHitTesting(false) }
                        .offset(x: (slide - width) * 0.3)
                        .allowsHitTesting(false)
                }
                currentPage
                    .overlay { Color.black.opacity(slide < 0 ? 0.08 * -slide / max(width, 1) : 0).allowsHitTesting(false) }
                    .overlay(alignment: .leading) { if slide > 0 { PageEdge() } }
                    .offset(x: slide > 0 ? slide : slide * 0.3)
                if slide < 0, let id = target(back: false) {
                    snapshot(drawn[id])
                        .overlay(alignment: .leading) { PageEdge() }
                        .offset(x: width + slide)
                        .allowsHitTesting(false)
                }
            }
            .onGeometryChange(for: CGFloat.self) { $0.size.width } action: { width = $0 }
            .background(SystemMargin { margin = $0 })
            // A strip along each edge takes the swipe, so a row under the finger is not tapped as well.
            .overlay(alignment: .leading) { edgeStrip(forward: false) }
            .overlay(alignment: .trailing) { edgeStrip(forward: true) }
            // In the safe area, so the list hangs just below the query capsule.
            .overlay(alignment: .top) { suggestions }
            .safeAreaBar(edge: .top) { searchBar }
            .safeAreaBar(edge: .bottom) { statusBar }
            .navigationTitle(page.kind == .home ? "LeXi" : "")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { toolbar }
            .sheet(isPresented: $showSettings) { SettingsView(store: store, speech: speech) }
        }
        .onChange(of: store.view.request) { _, _ in pageChanged() }
        .onChange(of: store.view.revision) { _, _ in pageDrawn() }
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

    // The live page, or, right after a slide, the page it moved to as drawn until Core shows it.
    @ViewBuilder private var currentPage: some View {
        if let arriving, let page = drawn[arriving] {
            snapshot(page)
        } else {
            ScrollView {
                content
                    .padding(.horizontal, margin).padding(.top, 4).padding(.bottom, 24)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            .scrollDismissesKeyboard(.interactively)
            .background(Color(uiColor: .systemGroupedBackground))
        }
    }

    private func snapshot(_ page: QueryView?) -> some View {
        PageSnapshot(store: store, speech: speech, page: page, margin: margin).equatable()
    }

    // The page a slide would go to, when it has been drawn (a host without page ids slides nothing).
    private func target(back: Bool) -> Int64? {
        let navigation = store.view.navigation
        guard let id = back ? navigation.backId : navigation.forwardId, id >= 0, back ? navigation.canBack : navigation.canForward, drawn[id] != nil else { return nil }
        return id
    }

    // Keeps each page's latest view. Core moves to a page a moment before drawing it, so a view with
    // another page's id but the same request still shows the page being left; taking it as the new
    // page's would flash the old page and keep it as that page's snapshot.
    private func pageDrawn() {
        let view = store.view
        guard let id = view.navigation.pageId, view.request != shown.request || id == shown.page else { return }
        shown = (view.request, id)
        drawn[id] = view
        // Core keeps 100 pages; the oldest snapshots go first.
        while drawn.count > 100, let oldest = drawn.keys.min() { drawn[oldest] = nil }
        if arriving == id { arriving = nil }
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
    private var status: String { speech.status.isEmpty ? page.status : speech.status }
    // The page on screen: right after a slide, the one it landed on, so the title and the status line
    // change with the page rather than when Core answers.
    private var page: QueryView { arriving.flatMap { drawn[$0] } ?? store.view }

    // The Mac footer: the soft matrix beside the status; nothing spins in the query capsule.
    @ViewBuilder private var statusBar: some View {
        if page.kind != .home && !status.isEmpty {
            HStack(spacing: 8) {
                QueryActivityMark(activity: page.progress?.activity ?? .idle, size: 14).foregroundStyle(.secondary)
                Text(status).font(.footnote).foregroundStyle(.secondary).lineLimit(1)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, margin + 4).padding(.vertical, 6)
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
        .padding(.horizontal, margin).padding(.top, 2).padding(.bottom, 6)
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
            .padding(.horizontal, margin).padding(.top, 6)
        }
    }

    // Back from the left edge and forward from the right, once the drag is mostly sideways and only
    // when there is a page that way. The page follows the finger; letting go past a third of the
    // width, or with a flick, finishes the move, otherwise it springs back.
    @ViewBuilder private func edgeStrip(forward: Bool) -> some View {
        let navigation = store.view.navigation
        if arriving == nil && (forward ? navigation.canForward : navigation.canBack) {
            Color.clear.frame(width: Self.edge).contentShape(.rect)
                .gesture(DragGesture(minimumDistance: 8)
                    .onChanged { value in
                        let dx = value.translation.width
                        guard abs(dx) > abs(value.translation.height) * 1.5 else { return }
                        slide = forward ? max(min(dx, 0), -width) : min(max(dx, 0), width)
                    }
                    .onEnded { value in
                        // Speed towards the page being revealed; a flick back cancels even past a third.
                        let speed = forward ? -value.velocity.width : value.velocity.width
                        let done = slide != 0 && (speed > 400 || abs(slide) > width / 3 && speed > -400)
                        if done { finish(back: !forward, velocity: value.velocity.width) }
                        else { withAnimation(Self.carry(value.velocity.width, from: slide, to: 0)) { slide = 0 } }
                    })
                .accessibilityHidden(true)
        }
    }

    // Completes a move with the slide: the page leaves (or arrives) and the one revealed is drawn
    // in place until Core's view for it arrives, so nothing jumps. The query capsule changes as the
    // slide sets off and the title as it lands, not when Core answers.
    private func finish(back: Bool, velocity: CGFloat = 0) {
        guard arriving == nil else { return }
        guard let destination = target(back: back), let page = drawn[destination] else { navigate(back ? -1 : 1); slide = 0; return }
        let end = back ? width : -width
        setText(page.kind == .home ? "" : page.input)
        withAnimation(Self.carry(velocity, from: slide, to: end)) { slide = end } completion: {
            arriving = destination
            var instant = Transaction(); instant.disablesAnimations = true
            withTransaction(instant) { slide = 0 }
            navigate(back ? -1 : 1)
        }
    }

    // A spring that leaves at the finger's speed, as UIKit's navigation does, and settles without a
    // bounce; starting from rest instead made the page pause where the finger let go.
    private static func carry(_ velocity: CGFloat, from start: CGFloat, to end: CGFloat) -> Animation {
        let distance = end - start
        let relative = abs(distance) < 1 ? 0 : min(max(velocity / distance, 0), 30)
        return .interpolatingSpring(duration: 0.35, bounce: 0, initialVelocity: relative)
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
            Button { finish(back: true) } label: { Image(systemName: "chevron.backward") }
                .disabled(!navigation.canBack).accessibilityLabel("后退")
            Button { finish(back: false) } label: { Image(systemName: "chevron.forward") }
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
            // Without an answer no view will replace the page a slide arrived at.
            guard let page = await store.navigate(direction) else { arriving = nil; return }
            if page.query.isEmpty { setText("") }
        }
    }

    // A new page (from a link, history or back/forward) puts the keyboard away so the result shows.
    private func pageChanged() {
        speech.stop()
        if store.view.kind != .home { setText(store.view.input); searching = false }
    }
}

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

// Reports the system's minimum side margin; SwiftUI's default padding is 16 pt on every iPhone.
private struct SystemMargin: UIViewRepresentable {
    let changed: (CGFloat) -> Void
    func makeUIView(context: Context) -> Probe { Probe(changed: changed) }
    func updateUIView(_ probe: Probe, context: Context) {}

    final class Probe: UIView {
        let changed: (CGFloat) -> Void
        private var last: CGFloat?
        init(changed: @escaping (CGFloat) -> Void) { self.changed = changed; super.init(frame: .zero); isUserInteractionEnabled = false }
        required init?(coder: NSCoder) { nil }
        override func didMoveToWindow() { super.didMoveToWindow(); report() }
        override func layoutSubviews() { super.layoutSubviews(); report() }
        private func report() {
            guard let margin = window?.rootViewController?.systemMinimumLayoutMargins.leading, margin > 0, margin != last else { return }
            last = margin
            DispatchQueue.main.async { [changed] in changed(margin) }
        }
    }
}

// A page as last drawn, beneath or ahead of a slide. It is redrawn when that page changes, not on
// every step of the finger.
private struct PageSnapshot: View, Equatable {
    let store: LexiStore
    let speech: SpeechController
    let page: QueryView?
    let margin: CGFloat

    nonisolated static func == (a: Self, b: Self) -> Bool {
        a.page?.navigation.pageId == b.page?.navigation.pageId && a.page?.revision == b.page?.revision && a.margin == b.margin
    }

    var body: some View {
        ScrollView {
            Group {
                if let page, page.kind != .home { EntryView(store: store, speech: speech, shown: page) }
                else { HistoryView(store: store) { _ in } }
            }
            .padding(.horizontal, margin).padding(.top, 4).padding(.bottom, 24)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .scrollDisabled(true)
        .background(Color(uiColor: .systemGroupedBackground))
    }
}

// The soft shade along a sliding page's leading edge. A gradient, because a shadow would be worked
// out from the whole page on every frame.
private struct PageEdge: View {
    var body: some View {
        LinearGradient(colors: [.black.opacity(0), .black.opacity(0.16)], startPoint: .leading, endPoint: .trailing)
            .frame(width: 12).offset(x: -12).allowsHitTesting(false)
    }
}
