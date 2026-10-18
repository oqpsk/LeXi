import AppKit
import ApplicationServices

// What the reader meant to look up when they pressed ⌘2 in another app: the selection there (with
// Accessibility permission) or text they copied a moment earlier. It is read once, while the other
// app is still in front, and handed to the query window when it comes forward. Core decides whether
// the text is looked up at once, only fills the query field or is ignored (query.candidate).
@MainActor public final class InstantLookup {
    public static let shared = InstantLookup()
    public static let clipboardKey = "lookupCopiedText"
    public static let selectionKey = "lookupSelectedText"
    // How long after a copy ⌘2 still means "look this up".
    static let freshness: TimeInterval = 8

    private let pasteboard: NSPasteboard
    private let active: () -> Bool
    private let now: () -> Date
    private var seen: Int
    private var changedAt: Date?
    private var consumed = -1
    private var pending: String?
    private var timer: Timer?

    // Tests pass their own pasteboard, clock and activity, so the reader's clipboard is never touched.
    init(pasteboard: NSPasteboard = .general, active: @escaping () -> Bool = { NSApplication.shared.isActive },
         now: @escaping () -> Date = Date.init) {
        self.pasteboard = pasteboard; self.active = active; self.now = now
        seen = pasteboard.changeCount
    }

    public static var copiedTextEnabled: Bool { UserDefaults.standard.object(forKey: clipboardKey) as? Bool ?? true }
    public static var selectedTextEnabled: Bool { UserDefaults.standard.object(forKey: selectionKey) as? Bool ?? false }

    // Only the change count is polled: it never reads the pasteboard or raises its privacy alert.
    public func watch() {
        guard timer == nil else { return }
        timer = Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { _ in
            MainActor.assumeIsolated { InstantLookup.shared.poll() }
        }
    }

    func poll() {
        let count = pasteboard.changeCount
        guard count != seen else { return }
        seen = count
        // LeXi's own copies (a TL;DR, a word form) are not something to look up when it comes back.
        changedAt = active() ? nil : now()
    }

    // Called by the global shortcut before LeXi activates, so the selection still belongs to the app in front.
    func capture() {
        pending = nil
        guard !active() else { return }
        if Self.selectedTextEnabled, let selected = SelectedText.read() { pending = selected; return }
        poll()
        guard Self.copiedTextEnabled, let changedAt, now().timeIntervalSince(changedAt) <= Self.freshness,
              seen != consumed else { return }
        consumed = seen
        pending = pasteboard.string(forType: .string)
    }

    // The query window takes the captured text once, when it comes forward.
    public func take() -> String? { defer { pending = nil }; return pending }
}

// The selected text of the focused element in the frontmost app, through the Accessibility API.
public enum SelectedText {
    public static var trusted: Bool { AXIsProcessTrusted() }
    // Shows the system prompt that leads to Privacy & Security → Accessibility.
    public static func requestTrust() {
        _ = AXIsProcessTrustedWithOptions(["AXTrustedCheckOptionPrompt": true] as CFDictionary)
    }

    static func read() -> String? {
        guard AXIsProcessTrusted() else { return nil }
        let system = AXUIElementCreateSystemWide()
        // An unresponsive app must not hold up ⌘2.
        AXUIElementSetMessagingTimeout(system, 0.25)
        var focused: CFTypeRef?
        guard AXUIElementCopyAttributeValue(system, kAXFocusedUIElementAttribute as CFString, &focused) == .success,
              let focused, CFGetTypeID(focused) == AXUIElementGetTypeID() else { return nil }
        let element = focused as! AXUIElement
        AXUIElementSetMessagingTimeout(element, 0.25)
        var selected: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXSelectedTextAttribute as CFString, &selected) == .success,
              let text = selected as? String, !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
        return text
    }
}
