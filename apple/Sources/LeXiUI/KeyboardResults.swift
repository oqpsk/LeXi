import AppKit
import LeXiKit
import SwiftUI

// The query window's keyboard focus for the senses, and the way back to the query field
// (Escape, or ↑ on the first sense).
struct SenseKeyboard {
    let focus: FocusState<String?>.Binding
    let leave: () -> Void
}

// Senses can be used from the keyboard after ↓ or Tab in the query field: ↑ ↓ move between them,
// Space reads the focused sense aloud (again to stop), Return opens or closes its examples, ⌘C copies
// it and Escape goes back to the query. The focused sense is tinted, like a sense being read.
struct KeyboardSense: ViewModifier {
    let sense: Sense
    let keys: [String]
    let keyboard: SenseKeyboard
    let store: LexiStore
    @Environment(SpeechController.self) private var speech: SpeechController?

    private var focus: FocusState<String?>.Binding { keyboard.focus }
    private func leave() { keyboard.leave() }

    private var focused: Bool { focus.wrappedValue == sense.key }
    // The English definition, or the Chinese one when there is none, is what Space reads.
    private var reading: (id: String, text: String) {
        sense.english.isEmpty ? (sense.key + ":chinese", sense.chinese) : (sense.key + ":english", sense.english)
    }

    func body(content: Content) -> some View {
        content
            .background {
                RoundedRectangle(cornerRadius: 8, style: .continuous)
                    .fill(Color.accentColor.opacity(focused ? 0.10 : 0))
                    .padding(.horizontal, -10).padding(.vertical, -6)
            }
            .focusable(interactions: .edit)
            .focusEffectDisabled()
            .focused(focus, equals: sense.key)
            .onKeyPress(.downArrow) { move(1) }
            .onKeyPress(.upArrow) { move(-1) }
            .onKeyPress(.escape) { leave(); return .handled }
            .onKeyPress(.space) { read(); return .handled }
            .onKeyPress(.return) { toggleExamples() }
            .onKeyPress(characters: .init(charactersIn: "c"), phases: .down) { press in
                guard press.modifiers == .command else { return .ignored }
                copyToPasteboard([sense.chinese, sense.english].filter { !$0.isEmpty }.joined(separator: "\n"))
                return .handled
            }
            .accessibilityHint("上下方向键切换义项，空格朗读，Return 展开例句，Command-C 复制，Escape 返回查询")
    }

    private func move(_ direction: Int) -> KeyPress.Result {
        guard let index = keys.firstIndex(of: sense.key) else { return .ignored }
        let next = index + direction
        if next < 0 { leave() } else if next < keys.count { focus.wrappedValue = keys[next] }
        return .handled
    }

    private func read() {
        guard let speech else { return }
        let target = reading
        if speech.speakingID == target.id { speech.stop("发音已停止"); return }
        speech.play(target.text, store.speechSelection, using: store, finished: "发音播放结束", id: target.id)
    }

    private func toggleExamples() -> KeyPress.Result {
        guard !sense.example.isEmpty || !sense.exampleChinese.isEmpty else { return .ignored }
        let key = sense.key + ":examples"
        store.setOpen(key, !store.isOpen(key))
        return .handled
    }
}

extension View {
    @ViewBuilder func keyboardSense(_ sense: Sense, keys: [String], keyboard: SenseKeyboard?, store: LexiStore) -> some View {
        if let keyboard { modifier(KeyboardSense(sense: sense, keys: keys, keyboard: keyboard, store: store)) } else { self }
    }
}
