import AppKit
import Carbon.HIToolbox
import ServiceManagement
import SwiftUI

// ⌘2 from any app brings LeXi forward, ready to type (MainView focuses the query on activation);
// pressed again while LeXi's window is in front, it hides LeXi and returns to the previous app, as
// Win+2 does on Windows. Text selected in that app, or copied just before, is looked up at once
// (InstantLookup). It is a system hot key: it takes precedence over every app's own ⌘2 and
// needs no Accessibility permission. Settings → 通用 can turn it off.
@MainActor public final class GlobalShortcut {
    public static let shared = GlobalShortcut()
    public static let key = "globalShortcut"
    public static let keys = "⌘2"

    // The query window, and a way to reopen it after it has been closed.
    weak var window: NSWindow?
    var reopen: (() -> Void)?
    // True when the setting asks for ⌘2 but another app already holds it as a system hot key.
    public private(set) var unavailable = false
    private var hotKey: EventHotKeyRef?
    private var handler: EventHandlerRef?

    public var enabled: Bool { UserDefaults.standard.object(forKey: Self.key) as? Bool ?? true }

    // Holds or releases ⌘2 to match the setting.
    public func apply() {
        if enabled { register(); InstantLookup.shared.watch() } else { release() }
    }

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
        unavailable = RegisterEventHotKey(UInt32(kVK_ANSI_2), UInt32(cmdKey), id, GetApplicationEventTarget(), 0, &hotKey) != noErr
        if unavailable { hotKey = nil }
    }

    private func release() {
        if let hotKey { UnregisterEventHotKey(hotKey) }
        hotKey = nil
        unavailable = false
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

// Starting LeXi at login keeps ⌘2 ready from the first moment after signing in.
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
