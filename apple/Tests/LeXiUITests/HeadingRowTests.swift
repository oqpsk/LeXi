import AppKit
import Foundation
import SwiftUI
import Testing
@testable import LeXiKit
@testable import LeXiUI

// Starting playback replaces the headword's speaker with a waveform. The buttons beside the
// headword must not move: the waveform has no text baseline of its own.
@MainActor @Suite struct HeadingActions {
    @Test func playbackKeepsTheHeadingButtonsInPlace() async throws {
        let file = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .appending(path: "LeXiKitTests/Fixtures/views.json")
        let views = try #require(JSONSerialization.jsonObject(with: Data(contentsOf: file)) as? [String: Any])
        var page = try #require(views["dictionary"] as? [String: Any])
        page["etymology"] = [["text": "From Serendip.", "caption": "词源", "linkLabel": "", "sourceUrl": "", "generated": true]]
        let view = try JSONDecoder().decode(QueryView.self, from: JSONSerialization.data(withJSONObject: page))
        let store = LexiStore()
        let speech = SpeechController(keys: SpeechFlow.Keys())
        let idle = try Self.render(view, store, speech)

        let host = SpeechFlow.Host(); host.hold = true
        speech.toggle(view.speechText, using: host)
        while host.held == nil { await Task.yield() }
        #expect(speech.speakingID == "heading")
        let playing = try Self.render(view, store, speech)
        speech.stop()
        host.held?.resume(returning: "/nonexistent/late.mp3")
        await speech.work?.value

        #expect(idle.width == playing.width && idle.height == playing.height, "the row keeps its size")
        // The etymology button is the row's rightmost 20 points.
        let columns = (idle.width - 40)..<idle.width
        #expect(Self.alpha(idle, columns) == Self.alpha(playing, columns), "the etymology button stays where it was")
    }

    private static func render(_ view: QueryView, _ store: LexiStore, _ speech: SpeechController) throws -> CGImage {
        let renderer = ImageRenderer(content: HeadingRow(view: view, store: store, speech: speech).fixedSize())
        renderer.scale = 2
        return try #require(renderer.cgImage)
    }

    private static func alpha(_ image: CGImage, _ columns: Range<Int>) -> [UInt8] {
        let width = image.width, height = image.height
        var pixels = [UInt8](repeating: 0, count: width * height * 4)
        let context = CGContext(data: &pixels, width: width, height: height, bitsPerComponent: 8, bytesPerRow: width * 4,
                                space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))
        return (0..<height).flatMap { y in columns.map { x in pixels[(y * width + x) * 4 + 3] } }
    }
}
