import AppKit
import LeXiKit
import LeXiUI
import SwiftUI

// Applies the saved appearance before any window appears, holds the global shortcut once running, and
// keeps LeXi running after its window closes.
@MainActor final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationWillFinishLaunching(_ notification: Notification) {
        AppearanceChoice.apply(UserDefaults.standard.string(forKey: AppearanceChoice.key) ?? AppearanceChoice.system.rawValue)
    }
    func applicationDidFinishLaunching(_ notification: Notification) {
        GlobalShortcut.shared.apply()
    }
    // Closing the query window keeps LeXi running, so the global shortcut and the Dock icon can bring it back.
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
}

@main
struct LeXiApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @State private var store = LexiStore()

    var body: some Scene {
        // A single query window; closing it keeps LeXi running and the Dock icon reopens it.
        Window("LeXi", id: "main") {
            MainView(store: store)
                .task { await start() }
        }
        .defaultSize(width: 720, height: 680)
        .windowBackgroundDragBehavior(.enabled)
        .commands { LexiCommands() }

        // Settings follow the system accent color; pink stays a brand accent in the query window.
        Settings {
            SettingsView(store: store)
        }
        .windowResizability(.contentMinSize)
    }

    private func start() async {
        guard store.phase == .starting else { return }
        guard let (executable, arguments) = HostLocation.resolve() else {
            store.failToStart("未找到内置的查询服务。请重新安装 LeXi；开发时可用 LEXI_HOST 指定 lexi-host。")
            return
        }
        await store.start(executable: executable, arguments: arguments)
    }
}

struct LexiCommands: Commands {
    @FocusedValue(\.lexiActions) private var actions

    var body: some Commands {
        CommandGroup(replacing: .newItem) {}
        CommandMenu("查询") {
            Button("查一个新词") { actions?.focusSearch() }.keyboardShortcut("l")
            Button("重新生成") { actions?.regenerate() }.keyboardShortcut("r").disabled(actions?.canRegenerate != true)
            Button("朗读词头") { actions?.speak() }.keyboardShortcut("d").disabled(actions?.canSpeak != true)
            Button(actions?.copyTitle ?? "复制 TL;DR") { actions?.copySummary() }.keyboardShortcut("c", modifiers: [.command, .shift])
                .disabled(actions?.canCopySummary != true)
            Divider()
            Button("后退") { actions?.back() }.keyboardShortcut("[").disabled(actions?.canBack != true)
            Button("前进") { actions?.forward() }.keyboardShortcut("]").disabled(actions?.canForward != true)
            Button("查询历史") { actions?.home() }.keyboardShortcut("y")
        }
    }
}
