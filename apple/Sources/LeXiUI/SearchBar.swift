import AppKit
import LeXiKit
import SwiftUI

// The suggestion the arrow keys are previewing, and what was typed before the preview started.
public struct SuggestionCursor: Equatable {
    var selection: Int?
    var draft: String?
    public init() {}
}

// One multi-line input for words, phrases and text; Lexi.Core decides lookup or translation.
// Return searches, Shift-Return (or the standard Option-Return) adds a line. Arrow keys preview suggestions without searching,
// Tab or a click accepts one for editing, Escape restores what was typed.
public struct SearchBar: View {
    @Bindable var store: LexiStore
    @Binding var text: String
    // Set before changing the text from outside (a new page's query) so it is not treated as typing.
    @Binding var programmatic: Bool
    @Binding var cursor: SuggestionCursor
    var focused: FocusState<Bool>.Binding
    let submit: (String) -> Void
    @State private var editing = false

    public init(store: LexiStore, text: Binding<String>, programmatic: Binding<Bool>, cursor: Binding<SuggestionCursor>,
                focused: FocusState<Bool>.Binding, submit: @escaping (String) -> Void) {
        self.store = store; self._text = text; self._programmatic = programmatic; self._cursor = cursor; self.focused = focused; self.submit = submit
    }

    public var body: some View {
        HStack(alignment: .center, spacing: 8) {
            Image(systemName: "magnifyingglass").font(.system(size: 17, weight: .medium)).foregroundStyle(.secondary).accessibilityHidden(true)
            TextField("查词或翻译", text: $text, axis: .vertical)
                .textFieldStyle(.plain)
                .font(.system(size: 19))
                .lineLimit(1...6)
                .focused(focused)
                // Keys belong to the input method while it is composing (pinyin, zhuyin…).
                .onKeyPress(.return, phases: .down) { press in
                    guard !Self.composing, press.modifiers.contains(.shift) else { return .ignored }
                    text.append("\n"); return .handled
                }
                .onSubmit { finish() }
                .onKeyPress(.downArrow) { !Self.composing && move(1) ? .handled : .ignored }
                .onKeyPress(.upArrow) { !Self.composing && move(-1) ? .handled : .ignored }
                .onKeyPress(.tab) { !Self.composing && accept() ? .handled : .ignored }
                .onKeyPress(.escape) { !Self.composing && dismiss(restore: true) ? .handled : .ignored }
                .onChange(of: text) { _, value in edited(value) }
                .accessibilityLabel("查询")
                .accessibilityHint("Return 查询，Shift-Return 换行")
            StatusRing(progress: store.view.progress)
            Button { finish() } label: {
                Image(systemName: "return").font(.system(size: 16, weight: .medium)).frame(width: 26, height: 26).contentShape(.rect)
            }
                .buttonStyle(.borderless)
                .foregroundStyle(.secondary)
                .help("查询 · Return")
                .accessibilityLabel("查询")
        }
        .padding(.horizontal, 18).padding(.vertical, 12)
        // A Spotlight-like capsule that grows into a rounded rectangle for multi-line text.
        .glassEffect(.regular.interactive(), in: .rect(cornerRadius: 24, style: .continuous))
        .onChange(of: store.view.request) { _, _ in editing = false }
        .onChange(of: focused.wrappedValue) { _, active in if !active { editing = false; store.clearInputPreview() } }
        .onReceive(NotificationCenter.default.publisher(for: NSText.didChangeNotification)) { _ in
            // Committing marked text may leave the bound string unchanged. The notification
            // supplies that final composition boundary without reacting to other text fields.
            guard editing && focused.wrappedValue else { return }
            if Self.composing { store.clearInputPreview() } else { store.previewInput(text) }
        }
    }

    private static var composing: Bool {
        (NSApp.keyWindow?.firstResponder as? NSTextView)?.hasMarkedText() == true
    }

    // Real edits start a fresh candidate list; previews keep the original list intact.
    private func edited(_ value: String) {
        if programmatic { programmatic = false; return }
        cursor = SuggestionCursor()
        editing = true
        if Self.composing { store.clearInputPreview() } else { store.previewInput(value) }
        if value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            store.clearSuggestions()
            if store.view.kind != .home { Task { await store.home() } }
            return
        }
        Task { await store.suggest(value) }
    }

    private func finish() {
        accept(); editing = false; store.clearSuggestions(); submit(text)
    }

    private func setText(_ value: String) { if text != value { programmatic = true; text = value } }

    private func move(_ direction: Int) -> Bool {
        guard !store.suggestions.isEmpty else { return false }
        let next = (cursor.selection ?? -1) + direction
        if next < 0 {
            if let draft = cursor.draft { setText(draft) }
            cursor = SuggestionCursor()
        } else {
            if cursor.draft == nil { cursor.draft = text }
            let selection = min(next, store.suggestions.count - 1)
            cursor.selection = selection
            setText(store.suggestions[selection])
        }
        editing = true; store.previewInput(text)
        return true
    }

    @discardableResult private func accept() -> Bool {
        guard let selection = cursor.selection, selection < store.suggestions.count else { return false }
        setText(store.suggestions[selection])
        dismiss(restore: false)
        editing = true; store.previewInput(text)
        return true
    }

    @discardableResult private func dismiss(restore: Bool) -> Bool {
        guard !store.suggestions.isEmpty || store.inputPreview != nil else { return false }
        editing = false
        if restore, let draft = cursor.draft { setText(draft) }
        cursor = SuggestionCursor()
        store.clearSuggestions()
        return true
    }
}

// The candidates under the query bar while typing in it. The page places it in the content area
// just below the bar, outside the bar's own layout, so it always hangs down over the results.
public struct SuggestionList: View {
    let words: [String]
    let selection: Int?
    let pick: (Int) -> Void

    public init(words: [String], selection: Int?, pick: @escaping (Int) -> Void) {
        self.words = words; self.selection = selection; self.pick = pick
    }

    public var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ForEach(Array(words.enumerated()), id: \.offset) { index, word in
                Button { pick(index) } label: {
                    Text(word).font(LexiTheme.lexical(16)).frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.horizontal, 12).padding(.vertical, 6)
                        .background(selection == index ? AnyShapeStyle(.selection) : AnyShapeStyle(.clear), in: .rect(cornerRadius: 10, style: .continuous))
                        .contentShape(.rect)
                }
                .buttonStyle(.plain)
            }
        }
        .padding(6)
        .frame(width: 320, alignment: .leading)
        .glassEffect(.regular, in: .rect(cornerRadius: 18, style: .continuous))
        .accessibilityElement(children: .contain)
        .accessibilityLabel("输入建议")
        .accessibilityHint("上下方向键预览，Tab 选用，Return 查询，Escape 还原")
    }
}

// Indeterminate while the query runs, then a check or an incomplete symbol. Details and stages
// are in a popover and in the accessibility label; there are no percentages or estimates.
struct StatusRing: View {
    let progress: QueryProgress?
    @State private var showDetails = false

    var body: some View {
        if let progress {
            Button { showDetails.toggle() } label: {
                Group {
                    if progress.busy { ProgressView().controlSize(.small) }
                    else { Image(systemName: progress.warning ? "exclamationmark.circle" : "checkmark.circle")
                        .foregroundStyle(progress.warning ? AnyShapeStyle(.orange) : AnyShapeStyle(.secondary)) }
                }
                .frame(width: 20, height: 20)
            }
            .buttonStyle(.borderless)
            .help(progress.title + " · " + progress.detail)
            .accessibilityLabel(progress.accessibleName)
            .popover(isPresented: $showDetails, arrowEdge: .bottom) {
                VStack(alignment: .leading, spacing: 6) {
                    Text(progress.title).font(.headline).foregroundStyle(progress.warning ? AnyShapeStyle(.orange) : AnyShapeStyle(.primary))
                    Text(progress.detail).font(.callout).foregroundStyle(.secondary).frame(maxWidth: 300, alignment: .leading)
                    Text(progress.stageLabels.joined(separator: "\n")).font(.caption).foregroundStyle(.secondary)
                }
                .padding(14)
            }
            .onChange(of: progress.title) { _, title in
                AccessibilityNotification.Announcement(title).post()
            }
        }
    }
}
