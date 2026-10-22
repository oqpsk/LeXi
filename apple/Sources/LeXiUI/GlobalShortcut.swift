import AppKit
import Carbon.HIToolbox
import ServiceManagement
import SwiftUI

// The global shortcut from any app brings LeXi forward, ready to type (MainView focuses the query on
// activation); pressed again while LeXi's window is in front, it hides LeXi and returns to the
// previous app, as Win+2 does on Windows. Text selected in that app, or copied just before, is looked
// up at once (InstantLookup). It is a system hot key: it takes precedence over the app in front and
// needs no Accessibility permission. ⌥2 by default — Option sits where the Win key does — leaving
// ⌘2 to the app in front; Settings → 通用 records another or turns it off.
@MainActor public final class GlobalShortcut {
    public static let shared = GlobalShortcut()
    public static let key = "globalShortcut"
    // The combination: a virtual key code and Carbon modifier flags.
    public static let keyCodeKey = "globalShortcutKeyCode"
    public static let modifiersKey = "globalShortcutModifiers"
    public static let defaultKeyCode = kVK_ANSI_2
    public static let defaultModifiers = optionKey

    public static var keyCode: Int { UserDefaults.standard.object(forKey: keyCodeKey) as? Int ?? defaultKeyCode }
    public static var modifiers: Int { UserDefaults.standard.object(forKey: modifiersKey) as? Int ?? defaultModifiers }
    public static var keys: String { describe(keyCode, modifiers) }

    // The query window, and a way to reopen it after it has been closed.
    weak var window: NSWindow?
    var reopen: (() -> Void)?
    // True when the setting asks for the shortcut but another app already holds it as a system hot key.
    public private(set) var unavailable = false
    private var hotKey: EventHotKeyRef?
    private var handler: EventHandlerRef?

    public var enabled: Bool { UserDefaults.standard.object(forKey: Self.key) as? Bool ?? true }

    // Holds or releases the shortcut to match the settings, again after it changes.
    public func apply() {
        release()
        if enabled { register(); InstantLookup.shared.watch() }
    }

    // Lets the next key press reach Settings while a new shortcut is recorded.
    public func suspend() { release() }

    private func register() {
        guard hotKey == nil else { return }
        if handler == nil {
            var pressed = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
            InstallEventHandler(GetApplicationEventTarget(), { _, _, _ in
                // Hot key events arrive on the main thread through the application event target.
                MainActor.assumeIsolated { GlobalShortcut.shared.toggle() }
                return noErr
            }, 1, &pressed, nil, &handler)
        }
        let id = EventHotKeyID(signature: 0x4C65_5869, id: 1) // "LeXi"
        unavailable = RegisterEventHotKey(UInt32(Self.keyCode), UInt32(Self.modifiers), id, GetApplicationEventTarget(), 0, &hotKey) != noErr
        if unavailable { hotKey = nil }
    }

    private func release() {
        if let hotKey { UnregisterEventHotKey(hotKey) }
        hotKey = nil
        unavailable = false
    }

    // A key press as a shortcut: with ⌘, ⌥ or ⌃ (⇧ may join them), or a function key alone.
    public static func shortcut(_ event: NSEvent) -> (keyCode: Int, modifiers: Int)? {
        let flags = event.modifierFlags
        var modifiers = 0
        if flags.contains(.control) { modifiers |= controlKey }
        if flags.contains(.option) { modifiers |= optionKey }
        if flags.contains(.shift) { modifiers |= shiftKey }
        if flags.contains(.command) { modifiers |= cmdKey }
        let code = Int(event.keyCode)
        guard modifiers & (cmdKey | optionKey | controlKey) != 0 || functionKeys[code] != nil else { return nil }
        return (code, modifiers)
    }

    // As macOS menus write it: ⌃⌥⇧⌘, then the key as the current keyboard layout labels it.
    public static func describe(_ keyCode: Int, _ modifiers: Int) -> String {
        var text = ""
        if modifiers & controlKey != 0 { text += "⌃" }
        if modifiers & optionKey != 0 { text += "⌥" }
        if modifiers & shiftKey != 0 { text += "⇧" }
        if modifiers & cmdKey != 0 { text += "⌘" }
        return text + (functionKeys[keyCode] ?? namedKeys[keyCode] ?? character(keyCode))
    }

    private static let functionKeys = [kVK_F1: "F1", kVK_F2: "F2", kVK_F3: "F3", kVK_F4: "F4", kVK_F5: "F5", kVK_F6: "F6", kVK_F7: "F7",
        kVK_F8: "F8", kVK_F9: "F9", kVK_F10: "F10", kVK_F11: "F11", kVK_F12: "F12", kVK_F13: "F13", kVK_F14: "F14", kVK_F15: "F15",
        kVK_F16: "F16", kVK_F17: "F17", kVK_F18: "F18", kVK_F19: "F19", kVK_F20: "F20"]
    private static let namedKeys = [kVK_Space: "Space", kVK_Return: "↩", kVK_Tab: "⇥", kVK_Delete: "⌫", kVK_ForwardDelete: "⌦",
        kVK_Escape: "⎋", kVK_LeftArrow: "←", kVK_RightArrow: "→", kVK_UpArrow: "↑", kVK_DownArrow: "↓",
        kVK_Home: "↖", kVK_End: "↘", kVK_PageUp: "⇞", kVK_PageDown: "⇟"]

    // What the key types without modifiers on the current layout (an input method such as Pinyin
    // reports the layout beneath it).
    private static func character(_ keyCode: Int) -> String {
        guard let source = TISCopyCurrentKeyboardLayoutInputSource()?.takeRetainedValue(),
              let property = TISGetInputSourceProperty(source, kTISPropertyUnicodeKeyLayoutData) else { return "?" }
        let layout = Unmanaged<CFData>.fromOpaque(property).takeUnretainedValue()
        guard let bytes = CFDataGetBytePtr(layout) else { return "?" }
        var dead: UInt32 = 0
        var length = 0
        var characters = [UniChar](repeating: 0, count: 4)
        let status = bytes.withMemoryRebound(to: UCKeyboardLayout.self, capacity: 1) { keyboard in
            UCKeyTranslate(keyboard, UInt16(keyCode), UInt16(kUCKeyActionDisplay), 0, UInt32(LMGetKbdType()),
                           OptionBits(kUCKeyTranslateNoDeadKeysBit), &dead, characters.count, &length, &characters)
        }
        return status == noErr && length > 0 ? String(utf16CodeUnits: characters, count: length).uppercased() : "?"
    }

    public func toggle() {
        if NSApp.isActive, let window, window.isVisible, window.isKeyWindow, !window.isMiniaturized {
            NSApp.hide(nil)
            return
        }
        InstantLookup.shared.capture()
        NSApp.unhide(nil)
        NSApp.activate(ignoringOtherApps: true)
        if let window, window.isMiniaturized { window.deminiaturize(nil) }
        if let reopen { reopen() } else { window?.makeKeyAndOrderFront(nil) }
    }
}

// Starting LeXi at login keeps the global shortcut ready from the first moment after signing in.
public enum LoginItem {
    public static var enabled: Bool { SMAppService.mainApp.status == .enabled }
    public static var needsApproval: Bool { SMAppService.mainApp.status == .requiresApproval }

    // Returns an error message, or nil when the change took effect.
    public static func set(_ on: Bool) -> String? {
        do {
            if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
            return needsApproval ? "请在“系统设置 → 通用 → 登录项”中允许 LeXi。" : nil
        } catch {
            return on ? "无法将 LeXi 设为登录时打开。" : "无法取消登录时打开。"
        }
    }
}

// Tells the shortcut which window is the query window and how to reopen it.
struct ShortcutWindow: NSViewRepresentable {
    let reopen: () -> Void
    func makeNSView(context: Context) -> NSView { Reader(reopen: reopen) }
    func updateNSView(_ view: NSView, context: Context) {}

    private final class Reader: NSView {
        let reopen: () -> Void
        init(reopen: @escaping () -> Void) { self.reopen = reopen; super.init(frame: .zero) }
        required init?(coder: NSCoder) { nil }
        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            guard let window else { return }
            GlobalShortcut.shared.window = window
            GlobalShortcut.shared.reopen = reopen
        }
    }
}
