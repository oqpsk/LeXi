import LeXiKit
import SwiftUI

// Menu commands act on the focused window's query page.
public struct LexiActions {
    public let back, forward, home, regenerate, focusSearch: () -> Void
    public let canBack, canForward, canRegenerate: Bool
}
extension FocusedValues { @Entry public var lexiActions: LexiActions? }

// The query window. Liquid Glass is limited to the floating control layer (toolbar, search
// bar, footer controls and popovers); results sit on the normal window background.
public struct MainView: View {
    @Bindable var store: LexiStore
    @State private var speech = SpeechController()
    @State private var text = ""
    @State private var programmatic = false
    @State private var position = ScrollPosition(edge: .top)
    @State private var offset: CGFloat = 0
    @FocusState private var searchFocused: Bool

    public init(store: LexiStore) { self.store = store }

    public var body: some View {
        // Apple's layering: results are the content; the query capsule, the bottom controls and
        // the toolbar are Liquid Glass floating above them, with soft scroll edges.
        ScrollView {
            content.padding(.horizontal, 24).padding(.vertical, 12)
        }
        .scrollPosition($position)
        .onScrollGeometryChange(for: CGFloat.self, of: { $0.contentOffset.y }) { _, value in offset = value }
        .scrollEdgeEffectStyle(.soft, for: [.top, .bottom])
        .safeAreaBar(edge: .top) {
            SearchBar(store: store, text: $text, programmatic: $programmatic, focused: $searchFocused, submit: submit)
                .padding(.horizontal, 20).padding(.top, 2).padding(.bottom, 10)
        }
        .safeAreaBar(edge: .bottom) { bottomBar }
        .containerBackground(LexiTheme.windowBackground, for: .window)
        .toolbar { toolbar }
        .navigationTitle("LeXi")
        .frame(minWidth: 540, minHeight: 420)
        .onChange(of: store.view.request) { _, _ in pageChanged() }
        .onChange(of: store.view.status) { _, _ in speech.clearStatus() }
        .focusedSceneValue(\.lexiActions, LexiActions(back: { navigate(-1) }, forward: { navigate(1) }, home: goHome,
            regenerate: regenerate, focusSearch: { searchFocused = true },
            canBack: store.view.navigation.canBack, canForward: store.view.navigation.canForward, canRegenerate: canRegenerate))
        .onAppear { searchFocused = true }
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
                EntryView(store: store, speech: speech)
            }
        }
    }

    private var online: Bool { store.settings?.settings.online ?? true }
    private var canRegenerate: Bool { store.view.kind != .home && store.view.canRegenerate && online }

    // Speech messages share the status line with the query, as in the Windows status bar; the newer one shows.
    private var status: String { speech.status.isEmpty ? store.view.status : speech.status }

    // Status on the leading side; regeneration and the online mode as grouped glass controls.
    private var bottomBar: some View {
        HStack(spacing: 12) {
            Text(status)
                .font(.callout).foregroundStyle(.secondary)
                .lineLimit(1).truncationMode(.tail)
                .help(status)
                .accessibilityLabel("查询状态：" + status)
            Spacer(minLength: 8)
            GlassEffectContainer(spacing: 8) {
                HStack(spacing: 8) {
                    if store.view.kind != .home {
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
        position.scrollTo(edge: .top)
        if store.view.kind != .home { setText(store.view.input) }
    }
}
