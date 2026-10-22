import AppKit
import SwiftUI

// Connections come before the pages that choose among what they offer.
enum SettingsPage: String, CaseIterable, Identifiable {
    case general = "通用", oauth = "OAuth 连接", api = "API 连接", query = "查询与模型", speech = "发音", dictionary = "离线词典", data = "数据与迁移"
    var id: String { rawValue }
    var symbol: String {
        switch self {
        case .general: "gearshape"
        case .query: "text.magnifyingglass"
        case .oauth: "person.crop.circle"
        case .api: "network"
        case .speech: "speaker.wave.2"
        case .dictionary: "books.vertical"
        case .data: "externaldrive"
        }
    }
}

struct SettingsWindowMemory: NSViewRepresentable {
    func makeNSView(context: Context) -> NSView { MemoryView() }
    func updateNSView(_ view: NSView, context: Context) {}
    private final class MemoryView: NSView {
        private var restored = false
        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            guard let window, !restored else { return }; restored = true
            // A new name since the window became resizable: frames saved before were too short.
            window.setFrameAutosaveName("LeXi.SettingsWindow")
            window.setFrameUsingName("LeXi.SettingsWindow")
        }
    }
}
