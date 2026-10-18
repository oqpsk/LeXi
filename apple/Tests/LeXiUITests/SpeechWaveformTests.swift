import AppKit
import SwiftUI
import Testing
@testable import LeXiUI

@MainActor @Suite struct SpeechWaveformChecks {
    @Test(arguments: [14.0, 18.0]) func fiveBarsKeepTheirCenters(_ size: Double) {
        let box = CGRect(x: 0, y: 0, width: size, height: size)
        let waiting = SpeechWaveformShape(time: nil).bars(in: box)
        #expect(waiting.count == 5)
        for time in stride(from: 0.0, through: 5, by: 0.037) {
            let bars = SpeechWaveformShape(time: time).bars(in: box)
            #expect(bars.allSatisfy { $0.width > 0 && $0.height > 0 && box.contains($0) && abs($0.midY-box.midY) < 0.001 })
            #expect(zip(bars, waiting).allSatisfy { abs($0.0.midX-$0.1.midX) < 0.001 && abs($0.0.width-$0.1.width) < 0.001 })
        }
        #expect(SpeechWaveformShape(time: 0).bars(in: box) != SpeechWaveformShape(time: 0.4).bars(in: box))
        #expect(abs((waiting.first!.minX + waiting.last!.maxX)/2 - box.midX) < 0.001)
    }

    @Test(arguments: [14.0, 18.0]) func staticWaveformRendersWithoutAWindow(_ size: Double) throws {
        // accessibilityReduceMotion is a read-only system value; never mutate the user's setting.
        let renderer = ImageRenderer(content: SpeechWaveform(animate: false, size: size).foregroundStyle(.pink))
        let image = try #require(renderer.nsImage)
        #expect(image.size.width == size && image.size.height == size)
    }
}
