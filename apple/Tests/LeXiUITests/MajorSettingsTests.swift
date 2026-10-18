import AppKit
import Foundation
import Testing
@testable import LeXiKit
@testable import LeXiUI

// macOS details of the major settings update that the shared checks cannot see.
@MainActor @Suite struct MajorSettingsDetails {
    @Test func cacheSizesStayNumericOnTheChinesePage() {
        #expect(DataSettings.size(0) == "0 KB")
        #expect(DataSettings.size(300) == "不到 1 KB")
        #expect(DataSettings.size(6_000).hasSuffix("KB") && DataSettings.size(52_000_000).hasSuffix("MB"))
    }

    // 再次播放上一段 sits under 朗读 in every read-aloud menu, disabled until something was read.
    @Test func readAloudMenuOffersTheLastPassage() throws {
        let view = BlockTextView(usingTextLayoutManager: false)
        view.show("serendipity", .system(15))
        let click = try #require(NSEvent.mouseEvent(with: .rightMouseDown, location: .zero, modifierFlags: [], timestamp: 0,
                                                    windowNumber: 0, context: nil, eventNumber: 0, clickCount: 1, pressure: 1))
        let before = try #require(view.menu(for: click))
        #expect(before.items[0].title == "朗读" && before.items[1].title == "再次播放上一段" && before.items[1].action == nil)
        var replayed = false
        view.repeatLast = { replayed = true }
        let after = try #require(view.menu(for: click))
        let again = after.items[1]
        #expect(again.action != nil)
        _ = (again.target as AnyObject?)?.perform(again.action, with: again)
        #expect(replayed)
    }

    // A replay tints the block it came from again, wherever it is on the page.
    @Test func replayKeepsTheReadBlock() async {
        let speech = SpeechController(keys: SpeechFlow.Keys())
        let host = SpeechFlow.Host(); host.hold = true
        #expect(!speech.canRepeat)
        speech.play("A happy accident.", host.speechSelection, using: host, finished: "发音播放结束", id: "sense-1")
        while host.held == nil { await Task.yield() }
        speech.stop()
        host.held?.resume(returning: "/nonexistent/late.mp3"); host.held = nil
        await speech.work?.value
        #expect(speech.canRepeat && speech.speakingID == nil)
        speech.repeatLast(using: host)
        #expect(speech.speakingID == "sense-1")
        while host.held == nil { await Task.yield() }
        speech.stop()
        host.held?.resume(returning: "/nonexistent/late.mp3")
        await speech.work?.value
    }
}
