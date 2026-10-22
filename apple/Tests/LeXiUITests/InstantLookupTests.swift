import AppKit
import Foundation
import Testing
@testable import LeXiKit
@testable import LeXiUI

// The text the global shortcut picks up from a copy in another app. A private pasteboard stands in for the reader's.
@MainActor @Suite(.serialized) struct InstantLookupChecks {
    final class Clock { var date = Date(timeIntervalSince1970: 1_000_000); var active = false }

    private func make() -> (InstantLookup, NSPasteboard, Clock) {
        let board = NSPasteboard(name: .init("io.github.oqpsk.lexi.tests.\(UUID().uuidString)"))
        let clock = Clock()
        return (InstantLookup(pasteboard: board, active: { clock.active }, now: { clock.date }), board, clock)
    }
    private func copy(_ text: String, to board: NSPasteboard) { board.clearContents(); board.setString(text, forType: .string) }

    @Test func aFreshCopyIsTakenOnce() {
        let (lookup, board, clock) = make()
        copy("serendipity", to: board)
        clock.date += 2
        lookup.capture()
        #expect(lookup.take() == CapturedText(text: "serendipity"))
        #expect(lookup.take() == nil, "the window takes it once")
        lookup.capture()
        #expect(lookup.take() == nil, "the same copy is not looked up again on the next press")
        board.releaseGlobally()
    }

    @Test func oldCopiesAndLeXisOwnCopiesAreIgnored() {
        let (lookup, board, clock) = make()
        copy("ephemeral", to: board)
        lookup.poll()
        clock.date += InstantLookup.freshness + 1
        lookup.capture()
        #expect(lookup.take() == nil, "a copy from a while ago is not what the shortcut means")

        clock.active = true
        copy("TL;DR text", to: board)
        lookup.poll()
        clock.active = false
        lookup.capture()
        #expect(lookup.take() == nil, "what LeXi itself copied is not looked up when it comes back")
        board.releaseGlobally()
    }

    @Test func nothingIsReadWhileLeXiIsInFront() {
        let (lookup, board, clock) = make()
        copy("serendipity", to: board)
        clock.active = true
        lookup.capture()
        #expect(lookup.take() == nil)
        board.releaseGlobally()
    }

    @Test func theSettingTurnsItOff() {
        let (lookup, board, _) = make()
        let defaults = UserDefaults.standard
        let saved = defaults.object(forKey: InstantLookup.clipboardKey)
        defer { if let saved { defaults.set(saved, forKey: InstantLookup.clipboardKey) } else { defaults.removeObject(forKey: InstantLookup.clipboardKey) } }
        defaults.set(false, forKey: InstantLookup.clipboardKey)
        copy("serendipity", to: board)
        lookup.capture()
        #expect(lookup.take() == nil)
        board.releaseGlobally()
    }
}
