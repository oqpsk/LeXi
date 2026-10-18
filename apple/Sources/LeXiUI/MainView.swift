import AppKit
import LeXiKit
import SwiftUI

// Menu commands act on the focused window's query page.
public struct LexiActions {
    public let back, forward, home, regenerate, focusSearch, speak, copySummary: () -> Void
    public let canBack, canForward, canRegenerate, canSpeak, canCopySummary: Bool
    // "复制 TL;DR" on a dictionary page, "复制译文" on a translation.
    public let copyTitle: String
}
extension FocusedValues { @Entry public var lexiActions: LexiActions? }

// The query window: a pane of Liquid Glass over the desktop, with the toolbar, search bar and
// footer controls as glass on top of it and the results on grouped surfaces in between.
public struct MainView: View {
    @Bindable var store: LexiStore
    @State private var speech = SpeechController()
    @State private var text = ""
    @State private var programmatic = false
    @State private var cursor = SuggestionCursor()
    @State private var position = ScrollPosition(edge: .top)
    @State private var offset: CGFloat = 0
    @FocusState private var searchFocused: Bool
    @FocusState private var senseFocus: String?
    // A short confirmation in the status line, such as after copying.
    @State private var notice = ""
    @Environment(\.appearsActive) private var appearsActive
    @Environment(\.openWindow) private var openWindow

    public init(store: LexiStore) { self.store = store }

    public var body: some View {
        // Apple's layering: results are the content; the query capsule, the bottom controls and
        // the toolbar are Liquid Glass floating above them, with soft scroll edges.
        ScrollView {
            // Full width even when the page is narrow (an empty history), so the scroll area and
            // the suggestion list line up with the query bar.
            content.padding(.horizontal, 24).padding(.vertical, 12).frame(maxWidth: .infinity)
        }
        .scrollPosition($position)
        .onScrollGeometryChange(for: CGFloat.self, of: { $0.contentOffset.y }) { _, value in offset = value }
        .scrollEdgeEffectStyle(.soft, for: [.top, .bottom])
        // Within the safe area, so it starts just below the query bar and floats over the results.
        .overlay(alignment: .topLeading) {
            if searchFocused && !store.suggestions.isEmpty {
                SuggestionList(words: store.suggestions, selection: cursor.selection, pick: pickSuggestion)
                    .padding(.leading, 20).offset(y: -4)
            }
        }
        .safeAreaBar(edge: .top) {
            VStack(spacing: 8) {
                SearchBar(store: store, text: $text, programmatic: $programmatic, cursor: $cursor, focused: $searchFocused, submit: submit,
                          enterResults: enterResults)
                    .padding(.horizontal, 20).padding(.top, 2)
                if searchFocused, let preview = store.inputPreview {
                    Button { submit(preview.query) } label: { InputPreviewCard(preview: preview) }
                        .buttonStyle(.plain).padding(.horizontal, 20)
                }
            }.padding(.bottom, 10)
        }
        .safeAreaBar(edge: .bottom) { bottomBar }
        .glassWindow()
        // The global ⌘2 brings this window back, reopening it if it was closed.
        .background(ShortcutWindow { openWindow(id: "main") })
        .toolbar { toolbar }
        .navigationTitle("LeXi")
        .frame(minWidth: 540, minHeight: 420)
        .onChange(of: store.view.request) { _, _ in pageChanged() }
        .onChange(of: store.view.status) { _, _ in speech.clearStatus() }
        .focusedSceneValue(\.lexiActions, LexiActions(back: { navigate(-1) }, forward: { navigate(1) }, home: goHome,
            regenerate: regenerate, focusSearch: focusSearch, speak: speakHeadword, copySummary: copySummary,
            canBack: store.view.navigation.canBack, canForward: store.view.navigation.canForward, canRegenerate: canRegenerate,
            canSpeak: store.view.kind != .home && !store.view.speechText.isEmpty, canCopySummary: !summary.isEmpty,
            copyTitle: store.view.kind == .translation ? "复制译文" : "复制 TL;DR"))
        // No defaultFocus: it pulls focus back to the query field and would keep the senses from taking
        // the keyboard. Activation focuses the query instead.
        .onChange(of: appearsActive, initial: true) { _, active in if active { focusSearch(); lookUpSummoned() } }
        .onChange(of: senseFocus) { _, key in
            if let key { withAnimation(.easeOut(duration: 0.15)) { position.scrollTo(id: key, anchor: .center) } }
        }
    }

    @ViewBuilder private var content: some View {
        switch store.phase {
        case .failed(let message):
            ContentUnavailableView("LeXi 无法查询", systemImage: "exclamationmark.triangle", description: Text(message))
        case .starting:
            ProgressView().frame(maxWidth: .infinity).padding(.top, 40)
        case .ready:
            if store.view.kind == .home {
                HistoryView(store: store) { entry in setText(entry.query); submit(entry.query) }
            } else {
                EntryView(store: store, speech: speech, keyboard: SenseKeyboard(focus: $senseFocus, leave: focusSearch)).environment(speech)
            }
        }
    }

    private var online: Bool { store.settings?.settings.online ?? true }
    private var canRegenerate: Bool { store.view.kind != .home && store.view.canRegenerate && online }

    // Speech messages share the status line with the query, as in the Windows status bar; the newer one shows.
    private var status: String { !notice.isEmpty ? notice : speech.status.isEmpty ? store.view.status : speech.status }

    // Status on the leading side; regeneration and the online mode as grouped glass controls.
    private var bottomBar: some View {
        HStack(spacing: 12) {
            QueryActivityMark(activity: store.view.progress?.activity ?? .idle)
                .foregroundStyle(.secondary)
            Text(status)
                .font(.callout).foregroundStyle(.secondary)
                .lineLimit(1).truncationMode(.tail)
                .help(status)
                .accessibilityLabel("查询状态：" + status)
            Spacer(minLength: 8)
            GlassEffectContainer(spacing: 8) {
                HStack(spacing: 8) {
                    if store.view.kind != .home {
                        if store.view.canRetry == true {
                            Button("重试未完成", systemImage: "arrow.clockwise.circle") { speech.stop(); Task { await store.retryFailed() } }
                        }
                        Button(action: regenerate) { Label("重新生成", systemImage: "arrow.clockwise") }
                            .disabled(!canRegenerate)
                            .help(store.settings?.controls.regenerateTooltip ?? "")
                    }
                    // A two-state mode, shown by its icon and label; neutral glass in both states.
                    Button { Task { await store.setOnline(!online) } } label: {
                        Label(store.settings?.controls.onlineLabel ?? "在线补充", systemImage: online ? "globe" : "network.slash")
                    }
                    .help(store.settings?.controls.onlineTooltip ?? "")
                    .accessibilityAddTraits(.isToggle)
                    .accessibilityValue(online ? "已开启" : "已关闭")
                }
                .buttonStyle(.glass)
                .labelStyle(.titleAndIcon)
            }
        }
        .padding(.horizontal, 20).padding(.vertical, 10)
    }

    @ToolbarContentBuilder private var toolbar: some ToolbarContent {
        let navigation = store.view.navigation
        ToolbarItemGroup(placement: .navigation) {
            Button { navigate(-1) } label: { Label("后退", systemImage: "chevron.left") }
                .disabled(!navigation.canBack)
                .help(navigation.canBack ? "后退 · \(navigation.backLabel) · ⌘[" : "后退 · ⌘[")
            Button { navigate(1) } label: { Label("前进", systemImage: "chevron.right") }
                .disabled(!navigation.canForward)
                .help(navigation.canForward ? "前进 · \(navigation.forwardLabel) · ⌘]" : "前进 · ⌘]")
        }
        ToolbarItemGroup(placement: .primaryAction) {
            Button(action: goHome) { Label("查询历史", systemImage: "clock") }.help("查询历史 · ⌘Y")
            SettingsLink { Label("设置", systemImage: "gearshape") }.help("设置 · ⌘,")
        }
    }

    private func setText(_ value: String) { if text != value { programmatic = true; text = value } }

    // As on Windows, the query is ready whenever the window comes forward or ⌘L is pressed: the
    // cursor is in it with the last query selected, so the next word simply replaces it.
    private func focusSearch() {
        senseFocus = nil
        searchFocused = true
        DispatchQueue.main.async { (NSApp.keyWindow?.firstResponder as? NSTextView)?.selectAll(nil) }
    }

    private func pickSuggestion(_ index: Int) {
        guard index < store.suggestions.count else { return }
        cursor = SuggestionCursor()
        setText(store.suggestions[index])
        store.clearSuggestions()
        store.previewInput(text)
        searchFocused = true
    }

    // ↓ or Tab in the query field with no suggestions: the first sense takes the keyboard.
    private func enterResults() -> Bool {
        guard store.view.kind == .dictionary, let first = store.view.answers?.senses.first?.key else { return false }
        senseFocus = first
        return true
    }

    // Text selected or copied in another app before ⌘2 (InstantLookup), handled as Core decides:
    // looked up at once, or left in the query field, selected, to edit or confirm with Return.
    private func lookUpSummoned() {
        guard let raw = InstantLookup.shared.take() else { return }
        Task {
            guard let candidate = await store.candidate(raw) else { return }
            setText(candidate.text)
            if candidate.submit { submit(candidate.text) }
            focusSearch()
        }
    }

    // ⌘D reads the headword, or stops it.
    private func speakHeadword() {
        guard store.view.kind != .home, !store.view.speechText.isEmpty else { return }
        speech.toggle(store.view.speechText, using: store)
    }

    // ⌘⇧C copies what the page says in brief: the TL;DR, or the whole translation.
    private var summary: String {
        switch store.view.kind {
        case .dictionary: store.view.quickRead.visible ? store.view.quickRead.text : ""
        case .translation: store.view.translation?.paragraphs.joined(separator: "\n\n") ?? ""
        case .home: ""
        }
    }
    private func copySummary() {
        guard !summary.isEmpty else { return }
        copyToPasteboard(summary)
        notice = store.view.kind == .translation ? "已复制译文" : "已复制 TL;DR"
        Task { try? await Task.sleep(for: .seconds(2)); notice = "" }
    }

    private func submit(_ value: String) {
        guard !value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            Task { await store.submit(value) }; return
        }
        speech.stop()
        Task { await store.submit(value, scroll: offset) }
    }

    private func regenerate() { guard canRegenerate else { return }; speech.stop(); Task { await store.regenerate() } }

    private func goHome() {
        speech.stop()
        setText("")
        Task { await store.home(scroll: offset) }
        searchFocused = true
    }

    // A newer submission or the reader's own scrolling wins over restoring the old position.
    private func navigate(_ direction: Int) {
        Task {
            guard let page = await store.navigate(direction, scroll: offset) else { return }
            if page.query.isEmpty { setText("") }
            if offset == 0, page.scrollOffset > 0 { position.scrollTo(y: page.scrollOffset) }
        }
    }

    private func pageChanged() {
        speech.stop()
        senseFocus = nil
        position.scrollTo(edge: .top)
        if store.view.kind != .home { setText(store.view.input) }
    }
}
